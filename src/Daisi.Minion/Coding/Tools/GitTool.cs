using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class GitTool : IMinionTool
{
    public string Name => "git";
    public string Description => "Run git commands. Supports: status, diff, log, add, commit, branch, checkout. For safety, push and destructive commands are not supported.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["args"] = new JsonObject { ["type"] = "string", ["description"] = "Git arguments (e.g. 'status', 'diff', 'log --oneline -10')" },
        },
        ["required"] = new JsonArray("args"),
    };

    private static readonly HashSet<string> BlockedCommands = ["push", "reset", "clean", "rebase", "force-push"];

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var args = arguments["args"]?.GetValue<string>();
        if (string.IsNullOrEmpty(args))
            return ToolResult.Error("Missing required parameter: args");

        var firstWord = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (BlockedCommands.Contains(firstWord))
            return ToolResult.Error($"git {firstWord} is blocked for safety. Use the shell tool if you must.");

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(30000);

        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { return ToolResult.Error("git command timed out"); }

        var output = stdout.ToString().TrimEnd();
        return process.ExitCode == 0
            ? ToolResult.Success(string.IsNullOrEmpty(output) ? "(no output)" : output)
            : ToolResult.Error($"Exit {process.ExitCode}: {stderr.ToString().TrimEnd()}");
    }
}
