using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class FileEditTool : IMinionTool
{
    private readonly ToolSandbox? _sandbox;

    public FileEditTool(ToolSandbox? sandbox = null) => _sandbox = sandbox;

    public string Name => "file_edit";
    public string Description => "Perform a search-and-replace edit on a file. The old_string must match exactly (including whitespace). Use replace_all to replace all occurrences.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path to edit" },
            ["old_string"] = new JsonObject { ["type"] = "string", ["description"] = "Exact string to find and replace" },
            ["new_string"] = new JsonObject { ["type"] = "string", ["description"] = "Replacement string" },
            ["replace_all"] = new JsonObject { ["type"] = "boolean", ["description"] = "Replace all occurrences (default false)" },
        },
        ["required"] = new JsonArray("path", "old_string", "new_string"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var path = arguments["path"]?.GetValue<string>();
        var oldStr = arguments["old_string"]?.GetValue<string>();
        var newStr = arguments["new_string"]?.GetValue<string>();
        var replaceAll = ToolArgs.GetBool(arguments, "replace_all");

        if (string.IsNullOrEmpty(path))
            return ToolResult.Error("Missing required parameter: path");
        if (oldStr is null)
            return ToolResult.Error("Missing required parameter: old_string");
        if (newStr is null)
            return ToolResult.Error("Missing required parameter: new_string");

        try { path = _sandbox?.ResolvePath(path) ?? Path.GetFullPath(path); }
        catch (InvalidOperationException ex) { return ToolResult.Error(ex.Message); }

        if (!File.Exists(path))
            return ToolResult.Error($"File not found: {path}");

        var content = await File.ReadAllTextAsync(path, ct);

        // Normalize line endings — model produces \n but files may have \r\n
        content = content.Replace("\r\n", "\n");
        oldStr = oldStr.Replace("\r\n", "\n");
        newStr = newStr.Replace("\r\n", "\n");

        if (!content.Contains(oldStr, StringComparison.Ordinal))
            return ToolResult.Error("old_string not found in file");

        if (!replaceAll)
        {
            int first = content.IndexOf(oldStr, StringComparison.Ordinal);
            int second = content.IndexOf(oldStr, first + 1, StringComparison.Ordinal);
            if (second >= 0)
                return ToolResult.Error("old_string matches multiple locations. Provide more context or use replace_all.");
        }

        string newContent = replaceAll
            ? content.Replace(oldStr, newStr)
            : ReplaceFirst(content, oldStr, newStr);

        await File.WriteAllTextAsync(path, newContent, ct);

        int count = replaceAll ? CountOccurrences(content, oldStr) : 1;
        return ToolResult.Success($"Replaced {count} occurrence(s) in {path}");
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        int idx = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (idx < 0) return text;
        return string.Concat(text.AsSpan(0, idx), newValue, text.AsSpan(idx + oldValue.Length));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(value, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += value.Length;
        }
        return count;
    }
}
