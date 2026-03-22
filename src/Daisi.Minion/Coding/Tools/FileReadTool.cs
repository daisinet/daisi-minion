using System.Text;
using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class FileReadTool : IMinionTool
{
    public string Name => "file_read";
    public string Description => "Read a file from disk. Returns the file content with line numbers. Use offset and limit for large files.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Absolute or relative file path" },
            ["offset"] = new JsonObject { ["type"] = "integer", ["description"] = "Line number to start from (1-based, default 1)" },
            ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "Max lines to read (default 2000)" },
        },
        ["required"] = new JsonArray("path"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var path = arguments["path"]?.GetValue<string>();
        if (string.IsNullOrEmpty(path))
            return ToolResult.Error("Missing required parameter: path");

        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            return ToolResult.Error($"File not found: {path}");

        int offset = ToolArgs.GetInt(arguments, "offset", 1);
        int limit = ToolArgs.GetInt(arguments, "limit", 2000);
        if (offset < 1) offset = 1;

        var lines = await File.ReadAllLinesAsync(path, ct);
        var sb = new StringBuilder();
        int end = Math.Min(offset - 1 + limit, lines.Length);

        for (int i = offset - 1; i < end; i++)
            sb.AppendLine($"{i + 1,6}\t{lines[i]}");

        if (end < lines.Length)
            sb.AppendLine($"... ({lines.Length - end} more lines)");

        return ToolResult.Success(sb.ToString());
    }
}
