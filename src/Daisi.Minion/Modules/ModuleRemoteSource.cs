using DaisiGit.SDK;

namespace Daisi.Minion.Modules;

/// <summary>
/// Pulls modules from a DaisiGit repository (typically a fork).
/// Each top-level directory in the repo that contains a module.cs is a module.
/// Downloaded modules are cached to ~/.daisi-minion/modules/ so they work offline.
/// </summary>
public sealed class ModuleRemoteSource : IDisposable
{
    private readonly DaisiGitClient _client;
    private readonly string _owner;
    private readonly string _slug;
    private readonly string _branch;
    private readonly string _localModulesDir;
    private readonly Action<string> _log;

    /// <summary>
    /// Create a remote module source.
    /// </summary>
    /// <param name="serverUrl">DaisiGit server URL (e.g., https://git.daisi.ai)</param>
    /// <param name="apiKey">API key (dg_...) for authentication</param>
    /// <param name="modulesRepo">Repository as "owner/slug"</param>
    /// <param name="branch">Branch to pull from (default: main)</param>
    /// <param name="localModulesDir">Local cache directory for modules</param>
    /// <param name="log">Logging callback</param>
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

    /// <summary>
    /// Pull all modules from the remote repo to local cache.
    /// Returns the number of modules updated.
    /// </summary>
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
            // Only directories (mode "040000" = tree)
            if (entry.Mode != "040000") continue;

            ct.ThrowIfCancellationRequested();

            var moduleName = entry.Name;
            var remoteModulePath = $"{moduleName}/module.cs";

            try
            {
                var file = await _client.GetFileAsync(_owner, _slug, remoteModulePath, _branch);

                if (file.IsBinary || string.IsNullOrEmpty(file.Text))
                {
                    _log($"  Skipping {moduleName}: not a text file");
                    continue;
                }

                // Check if local copy is already current (compare by content hash)
                var localDir = Path.Combine(_localModulesDir, moduleName);
                var localPath = Path.Combine(localDir, "module.cs");

                if (File.Exists(localPath))
                {
                    var localContent = await File.ReadAllTextAsync(localPath, ct);
                    if (localContent == file.Text)
                    {
                        _log($"  {moduleName}: up to date");
                        continue;
                    }
                }

                // Write updated module
                Directory.CreateDirectory(localDir);
                await File.WriteAllTextAsync(localPath, file.Text, ct);

                // Also pull tests.cs if it exists
                await TryPullFileAsync(moduleName, "tests.cs", localDir, ct);

                // Store remote metadata for push-back
                var metaPath = Path.Combine(localDir, ".remote.json");
                var meta = System.Text.Json.JsonSerializer.Serialize(new
                {
                    server = $"{_client}",
                    repo = $"{_owner}/{_slug}",
                    branch = _branch,
                    sha = file.Sha,
                    pulledUtc = DateTime.UtcNow,
                });
                await File.WriteAllTextAsync(metaPath, meta, ct);

                _log($"  {moduleName}: updated (sha={file.Sha[..7]})");
                updated++;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Directory exists but no module.cs — skip
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

    /// <summary>
    /// Push a local module back to the remote repo (for Darwin evolution).
    /// Creates or updates the module.cs file in the repo via API.
    /// </summary>
    public async Task PushModuleAsync(string moduleName, CancellationToken ct = default)
    {
        var localPath = Path.Combine(_localModulesDir, moduleName, "module.cs");
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"Module not found: {localPath}");

        var content = await File.ReadAllTextAsync(localPath, ct);
        var remotePath = $"{moduleName}/module.cs";

        _log($"Pushing {moduleName} to {_owner}/{_slug}@{_branch}...");

        // Use the commit API to create/update the file
        // The DaisiGit API endpoint for file writes is:
        // POST /api/git/repos/{owner}/{slug}/contents/{path}
        // with { content, branch, message }
        // For now, we'll use the SDK's HTTP client directly
        await CommitFileAsync(remotePath, content, $"Update module: {moduleName}", ct);

        // Also push tests.cs if it exists
        var testsPath = Path.Combine(_localModulesDir, moduleName, "tests.cs");
        if (File.Exists(testsPath))
        {
            var testsContent = await File.ReadAllTextAsync(testsPath, ct);
            await CommitFileAsync($"{moduleName}/tests.cs", testsContent,
                $"Update tests for module: {moduleName}", ct);
        }

        _log($"  Pushed {moduleName}");
    }

    /// <summary>
    /// List modules available in the remote repo.
    /// </summary>
    public async Task<List<string>> ListRemoteModulesAsync(CancellationToken ct = default)
    {
        var tree = await _client.GetTreeAsync(_owner, _slug, _branch);
        var modules = new List<string>();

        foreach (var entry in tree.Entries)
        {
            if (entry.Mode == "040000") // directory
                modules.Add(entry.Name);
        }

        return modules;
    }

    private async Task TryPullFileAsync(string moduleName, string fileName, string localDir, CancellationToken ct)
    {
        try
        {
            var file = await _client.GetFileAsync(_owner, _slug, $"{moduleName}/{fileName}", _branch);
            if (!file.IsBinary && !string.IsNullOrEmpty(file.Text))
                await File.WriteAllTextAsync(Path.Combine(localDir, fileName), file.Text, ct);
        }
        catch { } // Optional file — ignore errors
    }

    private async Task CommitFileAsync(string path, string content, string message, CancellationToken ct)
    {
        // TODO: The DaisiGit API needs a file-write/commit endpoint.
        // For now, this is a placeholder that will be wired up when the
        // API supports single-file commits (or we use git push via smart HTTP).
        _log($"  [commit] {path}: {message}");
        await Task.CompletedTask;
    }

    public void Dispose() => _client.Dispose();
}
