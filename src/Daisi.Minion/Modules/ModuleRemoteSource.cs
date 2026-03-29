using DaisiGit.SDK;

namespace Daisi.Minion.Modules;

/// <summary>
/// Pulls and pushes modules to/from a DaisiGit repository via the REST API.
/// No local git clone needed — all operations are pure HTTP.
///
/// Branching model:
/// - Pull reads from the configured branch (default: main)
/// - Darwin creates a run branch (darwin/{minion-name}/{date}) for evolution
/// - Evolved modules are committed to the run branch via the contents API
/// - Darwin PRs the run branch back to main when scoring is good
/// </summary>
public sealed class ModuleRemoteSource : IDisposable
{
    private readonly DaisiGitClient _client;
    private readonly string _owner;
    private readonly string _slug;
    private readonly string _branch;
    private readonly string _localModulesDir;
    private readonly Action<string> _log;

    public ModuleRemoteSource(
        string serverUrl, string apiKey, string modulesRepo,
        string branch = "main", string? localModulesDir = null, Action<string>? log = null)
    {
        _client = new DaisiGitClient(serverUrl, apiKey);
        var parts = modulesRepo.Split('/', 2);
        _owner = parts[0];
        _slug = parts.Length > 1 ? parts[1] : throw new ArgumentException("modules_repo must be owner/slug");
        _branch = branch;
        _localModulesDir = localModulesDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");
        _log = log ?? (_ => { });
    }

    // ── Pull (read modules from remote → local cache) ──

    public async Task<int> PullAsync(CancellationToken ct = default)
    {
        _log($"Pulling modules from {_owner}/{_slug}@{_branch}...");

        TreeResult tree;
        try
        {
            tree = await _client.GetTreeAsync(_owner, _slug, _branch);
        }
        catch (Exception ex)
        {
            _log($"  Failed to browse repo: {ex.Message}");
            return 0;
        }

        int updated = 0;
        Directory.CreateDirectory(_localModulesDir);

        foreach (var entry in tree.Entries)
        {
            if (entry.Mode != "040000") continue;
            if (entry.Name is "reference" or "tests") continue;

            ct.ThrowIfCancellationRequested();
            var moduleName = entry.Name;

            try
            {
                var file = await _client.GetFileAsync(_owner, _slug, $"{moduleName}/module.cs", _branch);
                if (file.IsBinary || string.IsNullOrEmpty(file.Text)) continue;

                var localDir = Path.Combine(_localModulesDir, moduleName);
                var localPath = Path.Combine(localDir, "module.cs");

                if (File.Exists(localPath) && await File.ReadAllTextAsync(localPath, ct) == file.Text)
                {
                    _log($"  {moduleName}: up to date");
                    continue;
                }

                Directory.CreateDirectory(localDir);
                await File.WriteAllTextAsync(localPath, file.Text, ct);
                await TryPullFileAsync(moduleName, "tests.cs", localDir, ct);

                _log($"  {moduleName}: updated (sha={file.Sha[..7]})");
                updated++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }
            catch (Exception ex)
            {
                _log($"  {moduleName}: error — {ex.Message}");
            }
        }

        _log($"Pull complete: {updated} module(s) updated");
        return updated;
    }

    // ── Push (commit modules to remote via contents API) ──

    /// <summary>
    /// Create a Darwin evolution branch. Returns the branch name.
    /// </summary>
    public async Task<string> CreateDarwinBranchAsync(string minionName, CancellationToken ct = default)
    {
        var branchName = $"darwin/{minionName}/{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        _log($"Creating branch: {branchName} from {_branch}...");

        await _client.CreateBranchAsync(_owner, _slug, branchName, _branch);

        _log($"  Created branch: {branchName}");
        return branchName;
    }

    /// <summary>
    /// Push a module to a branch by committing its files via the contents API.
    /// </summary>
    public async Task PushModuleAsync(string moduleName, string? branch = null, CancellationToken ct = default)
    {
        var localModuleDir = Path.Combine(_localModulesDir, moduleName);
        var localModulePath = Path.Combine(localModuleDir, "module.cs");
        if (!File.Exists(localModulePath))
            throw new FileNotFoundException($"Module not found: {localModulePath}");

        var targetBranch = branch ?? _branch;
        _log($"Pushing {moduleName} to {_owner}/{_slug}@{targetBranch}...");

        // Commit module.cs
        var moduleContent = await File.ReadAllTextAsync(localModulePath, ct);
        await _client.WriteFileAsync(_owner, _slug, $"{moduleName}/module.cs",
            moduleContent, $"Evolve module: {moduleName}", targetBranch);

        // Commit tests.cs if it exists
        var testsPath = Path.Combine(localModuleDir, "tests.cs");
        if (File.Exists(testsPath))
        {
            var testsContent = await File.ReadAllTextAsync(testsPath, ct);
            await _client.WriteFileAsync(_owner, _slug, $"{moduleName}/tests.cs",
                testsContent, $"Update tests for: {moduleName}", targetBranch);
        }

        _log($"  Pushed {moduleName}");
    }

    /// <summary>
    /// Create a pull request from a Darwin branch back to main.
    /// </summary>
    public async Task<int> CreatePullRequestAsync(string sourceBranch, string title, string? description = null, CancellationToken ct = default)
    {
        var pr = await _client.CreatePullRequestAsync(_owner, _slug, title, sourceBranch, _branch, description);
        _log($"Created PR #{pr.Number}: {title}");
        return pr.Number;
    }

    /// <summary>
    /// List modules available in the remote repo.
    /// </summary>
    public async Task<List<string>> ListRemoteModulesAsync(CancellationToken ct = default)
    {
        var tree = await _client.GetTreeAsync(_owner, _slug, _branch);
        return tree.Entries
            .Where(e => e.Mode == "040000" && e.Name is not "reference" and not "tests")
            .Select(e => e.Name)
            .ToList();
    }

    // ── Helpers ──

    private async Task TryPullFileAsync(string moduleName, string fileName, string localDir, CancellationToken ct)
    {
        try
        {
            var file = await _client.GetFileAsync(_owner, _slug, $"{moduleName}/{fileName}", _branch);
            if (!file.IsBinary && !string.IsNullOrEmpty(file.Text))
                await File.WriteAllTextAsync(Path.Combine(localDir, fileName), file.Text, ct);
        }
        catch { }
    }

    public void Dispose() => _client.Dispose();
}
