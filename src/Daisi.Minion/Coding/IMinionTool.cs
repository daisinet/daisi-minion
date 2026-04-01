using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding;

/// <summary>
/// A tool that the coding assistant can invoke.
/// </summary>
public interface IMinionTool
{
    string Name { get; }
    string Description { get; }
    JsonObject ParametersSchema { get; }
    Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct);
}

/// <summary>
/// Result of a tool execution.
/// </summary>
public sealed class ToolResult
{
    public string Output { get; set; } = "";
    public bool IsError { get; set; }

    public static ToolResult Success(string output) => new() { Output = output };
    public static ToolResult Error(string error) => new() { Output = error, IsError = true };
}

/// <summary>
/// Helpers for reading tool arguments from JsonObject where values may be
/// strings (from Qwen XML parser) or native JSON types.
/// </summary>
public static class ToolArgs
{
    public static int GetInt(JsonObject args, string key, int defaultValue = 0)
    {
        var node = args[key];
        if (node is null) return defaultValue;
        try { return node.GetValue<int>(); }
        catch
        {
            var s = node.GetValue<string>();
            return int.TryParse(s, out var v) ? v : defaultValue;
        }
    }

    public static bool GetBool(JsonObject args, string key, bool defaultValue = false)
    {
        var node = args[key];
        if (node is null) return defaultValue;
        try { return node.GetValue<bool>(); }
        catch
        {
            var s = node.GetValue<string>();
            return bool.TryParse(s, out var v) ? v : defaultValue;
        }
    }

    public static string? GetString(JsonObject args, string key)
    {
        var node = args[key];
        if (node == null) return null;
        // Handle arrays: model sometimes sends ["item1", "item2"] instead of "item1\nitem2"
        if (node is System.Text.Json.Nodes.JsonArray arr)
            return string.Join("\n", arr.Select(n => n?.ToString() ?? ""));
        return node.GetValue<string>();
    }
}
