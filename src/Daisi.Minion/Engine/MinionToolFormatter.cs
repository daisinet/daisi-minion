using System.Text.Json;
using System.Text.Json.Nodes;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Minion's tool formatter. Supports both:
/// - Grammar mode: raw JSON {"name":..., "arguments":...} (no wrapper tags)
/// - Legacy mode: JSON inside &lt;tool_call&gt; tags
/// </summary>
public sealed class MinionToolFormatter : IToolFormatter
{
    public static readonly MinionToolFormatter Instance = new();

    public string FormatToolsBlock(IReadOnlyList<ToolDefinition> tools) =>
        ToolPromptFormatter.FormatToolsBlock(tools);

    public bool ContainsToolCalls(string text)
    {
        text = text.TrimStart();
        // Grammar mode: raw JSON starting with {"name"
        if (text.StartsWith("{") && text.Contains("\"name\""))
            return true;
        // Legacy mode: <tool_call> tags
        return text.Contains("<tool_call>", StringComparison.Ordinal);
    }

    public List<ToolCall> ParseToolCalls(string text)
    {
        text = text.TrimStart();

        // Grammar mode: raw JSON (no <tool_call> wrapper)
        if (text.StartsWith("{") && !text.Contains("<tool_call>"))
        {
            var calls = TryParseRawJson(text);
            if (calls.Count > 0) return calls;
        }

        // Legacy mode: QwenToolCallParser handles <tool_call> tags
        return QwenToolCallParser.Parse(text);
    }

    public ChatMessage FormatToolResult(string toolName, string result) =>
        new("tool", result);

    public string[] GetToolStopSequences() => ["</tool_call>"];

    private static List<ToolCall> TryParseRawJson(string text)
    {
        text = text.Trim();
        // Find the JSON object boundaries
        var end = text.LastIndexOf('}');
        if (end < 0) return [];
        var jsonStr = text[..(end + 1)];

        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            if (name == null) return [];

            var args = new JsonObject();
            if (root.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => JsonValue.Create(prop.Value.GetString()),
                        JsonValueKind.Number => JsonValue.Create(prop.Value.GetRawText()),
                        JsonValueKind.True => JsonValue.Create(true),
                        JsonValueKind.False => JsonValue.Create(false),
                        _ => JsonNode.Parse(prop.Value.GetRawText()),
                    };
                }
            }

            return [new ToolCall(name, args)];
        }
        catch
        {
            return [];
        }
    }
}
