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
