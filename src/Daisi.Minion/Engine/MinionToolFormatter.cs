using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Minion's tool formatter using JSON tool calls (model's native format).
/// Parses both JSON {"name":..., "arguments":...} and Qwen XML formats for compatibility.
/// </summary>
public sealed class MinionToolFormatter : IToolFormatter
{
    public static readonly MinionToolFormatter Instance = new();

    public string FormatToolsBlock(IReadOnlyList<ToolDefinition> tools) =>
        ToolPromptFormatter.FormatToolsBlock(tools);

    public bool ContainsToolCalls(string text) =>
        text.Contains("<tool_call>", StringComparison.Ordinal);

    public List<ToolCall> ParseToolCalls(string text)
    {
        // QwenToolCallParser handles missing </tool_call> (consumed by stop sequence)
        // and supports both JSON and XML formats
        return QwenToolCallParser.Parse(text);
    }

    public ChatMessage FormatToolResult(string toolName, string result) =>
        new("tool", result);

    public string[] GetToolStopSequences() => ["</tool_call>"];
}
