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
        // File tree omitted from system prompt to save tokens.
        // The model can use glob and file_read to explore as needed.

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
        BuildTree(sb, WorkingDirectory, "", 0, maxDepth: 3);
        return Task.FromResult(sb.ToString());
    }

    private static void BuildTree(StringBuilder sb, string dir, string indent, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        var dirName = Path.GetFileName(dir);
        if (dirName is ".git" or "node_modules" or "bin" or "obj" or ".vs" or ".idea")
            return;

        try
        {
            var entries = Directory.GetFileSystemEntries(dir)
                .OrderBy(e => !Directory.Exists(e))
                .ThenBy(e => Path.GetFileName(e))
                .Take(30)
                .ToList();

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    sb.AppendLine($"{indent}{name}/");
                    BuildTree(sb, entry, indent + "  ", depth + 1, maxDepth);
                }
                else
                {
                    sb.AppendLine($"{indent}{name}");
                }
            }
        }
        catch { }
    }
}
