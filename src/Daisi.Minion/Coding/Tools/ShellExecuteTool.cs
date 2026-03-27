using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class ShellExecuteTool : IMinionTool
{
    private readonly ToolSandbox? _sandbox;

    public ShellExecuteTool(ToolSandbox? sandbox = null) => _sandbox = sandbox;

    public string Name => "shell";
    public string Description => "Execute a shell command and return its output. Use for builds, tests, git operations, or any terminal command.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["command"] = new JsonObject { ["type"] = "string", ["description"] = "The shell command to execute" },
            ["timeout_ms"] = new JsonObject { ["type"] = "integer", ["description"] = "Timeout in milliseconds (default 120000)" },
        },
        ["required"] = new JsonArray("command"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var command = arguments["command"]?.GetValue<string>();
        if (string.IsNullOrEmpty(command))
            return ToolResult.Error("Missing required parameter: command");

        var timeoutMs = ToolArgs.GetInt(arguments, "timeout_ms", 120000);
        var workDir = _sandbox?.Root ?? Directory.GetCurrentDirectory();

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
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
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return ToolResult.Error($"Command timed out after {timeoutMs}ms");
        }

        var output = stdout.ToString();
        var errors = stderr.ToString();

        const int maxOutputChars = 8000;
        if (output.Length > maxOutputChars)
            output = output[..maxOutputChars] + $"\n... (truncated, {output.Length - maxOutputChars} chars omitted)";
        if (errors.Length > maxOutputChars)
            errors = errors[..maxOutputChars] + $"\n... (truncated)";

        if (process.ExitCode != 0)
        {
            var combined = string.IsNullOrEmpty(errors) ? output : $"{output}\nSTDERR:\n{errors}";
            return ToolResult.Error($"Exit code {process.ExitCode}:\n{combined}".TrimEnd());
        }

        return ToolResult.Success(string.IsNullOrEmpty(output) ? "(no output)" : output.TrimEnd());
    }
}
