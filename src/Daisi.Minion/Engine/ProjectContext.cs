using System.Diagnostics;
using System.Text;

namespace Daisi.Minion.Engine;

/// <summary>
/// Gathers project context from the working directory for the system prompt.
/// </summary>
public sealed class ProjectContext
{
    public string WorkingDirectory { get; }
    public string? GitBranch { get; private set; }
    public string? GitStatus { get; private set; }
    public string? FileTree { get; private set; }

    public ProjectContext(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
    }

    /// <summary>
    /// Refresh all context information.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        GitBranch = await RunGit("rev-parse --abbrev-ref HEAD", ct);
        GitStatus = await RunGit("status --short", ct);
        FileTree = await BuildFileTree(ct);
    }

    /// <summary>
    /// Build the system prompt section describing the project context.
    /// </summary>
    public string ToSystemPromptSection()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Environment");
        sb.AppendLine($"- Working directory: {WorkingDirectory}");

        if (!string.IsNullOrEmpty(GitBranch))
            sb.AppendLine($"- Git branch: {GitBranch}");
        if (!string.IsNullOrEmpty(GitStatus))
        {
            sb.AppendLine("- Git status:");
            sb.AppendLine(GitStatus);
        }
        if (!string.IsNullOrEmpty(FileTree))
        {
            sb.AppendLine("- Files:");
            sb.AppendLine(FileTree);
        }

        return sb.ToString();
    }

    private async Task<string?> RunGit(string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = WorkingDirectory,
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch { return null; }
    }

    private Task<string> BuildFileTree(CancellationToken ct)
    {
        var sb = new StringBuilder();
        try
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".git", "node_modules", "bin", "obj", ".vs", ".idea" };

            var entries = Directory.GetFileSystemEntries(WorkingDirectory)
                .Select(Path.GetFileName)
                .Where(n => n != null && !skip.Contains(n!))
                .OrderBy(n => n)
                .Take(30)
                .ToList();

            foreach (var name in entries)
                sb.AppendLine($"  {name}");
        }
        catch { }
        return Task.FromResult(sb.ToString());
    }
}
