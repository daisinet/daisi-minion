using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Minion's tool formatter. Tool results are formatted as ChatMessages with "tool" role,
/// which the MinionChatRenderer wraps in &lt;tool_response&gt; blocks inside user messages.
/// </summary>
public sealed class MinionToolFormatter : IToolFormatter
{
    public static readonly MinionToolFormatter Instance = new();

    public string FormatToolsBlock(IReadOnlyList<ToolDefinition> tools) =>
        ToolPromptFormatter.FormatToolsBlock(tools);

    public bool ContainsToolCalls(string text) =>
        QwenToolCallParser.ContainsToolCalls(text);

    public List<ToolCall> ParseToolCalls(string text) =>
        QwenToolCallParser.Parse(text);

    public ChatMessage FormatToolResult(string toolName, string result) =>
        new("tool", result);

    public string[] GetToolStopSequences() => ["</tool_call>"];
}
