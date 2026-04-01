using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class FileWriteTool : IMinionTool
{
    private readonly ToolSandbox? _sandbox;

    public FileWriteTool(ToolSandbox? sandbox = null) => _sandbox = sandbox;

    public string Name => "file_write";
    public string Description => "Write content to a file. Creates the file if it doesn't exist, overwrites if it does. Creates parent directories as needed.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Absolute or relative file path" },
            ["content"] = new JsonObject { ["type"] = "string", ["description"] = "Content to write" },
        },
        ["required"] = new JsonArray("path", "content"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var path = arguments["path"]?.GetValue<string>();
        var content = arguments["content"]?.GetValue<string>()
            ?? arguments["file_content"]?.GetValue<string>(); // alias: models sometimes use file_content

        if (string.IsNullOrEmpty(path))
            return ToolResult.Error("Missing required parameter: path");
        if (content is null)
            return ToolResult.Error("Missing required parameter: content");

        try { path = _sandbox?.ResolvePath(path) ?? Path.GetFullPath(path); }
        catch (InvalidOperationException ex) { return ToolResult.Error(ex.Message); }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Validate structure before writing
        var validationErrors = FileValidator.Validate(path, content);
        if (validationErrors != null)
        {
            var lineCount = content.Split('\n').Length;
            return ToolResult.Error(
                $"NOT WRITTEN — {path} ({lineCount} lines) has structural errors:\n"
                + string.Join("\n", validationErrors.Select(e => $"  - {e}"))
                + "\n\nFix these errors and call file_write again.");
        }

        bool existed = File.Exists(path);
        await File.WriteAllTextAsync(path, content, ct);

        var lines = content.Split('\n').Length;
        return ToolResult.Success(existed
            ? $"Overwrote {path} ({lines} lines)"
            : $"Created {path} ({lines} lines)");
    }
}
