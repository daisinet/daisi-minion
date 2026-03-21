using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Parses Qwen 3.5 native XML-style tool calls:
///
/// <![CDATA[
/// <tool_call>
/// <function=file_write>
/// <parameter=path>
/// index.html
/// </parameter>
/// <parameter=content>
/// file contents here
/// </parameter>
/// </function>
/// </tool_call>
/// ]]>
///
/// Also falls back to JSON format for compatibility.
/// </summary>
public static partial class QwenToolCallParser
{
    public static bool ContainsToolCalls(string text) =>
        text.Contains("<tool_call>", StringComparison.Ordinal);

    public static List<ToolCall> Parse(string text)
    {
        var results = new List<ToolCall>();

        // Try regex with closing tag first
        var blocks = ToolCallBlockRegex().Matches(text);
        if (blocks.Count == 0)
        {
            // Stop sequence may have consumed </tool_call> — try without it
            var openIdx = text.IndexOf("<tool_call>", StringComparison.Ordinal);
            if (openIdx >= 0)
            {
                var inner = text[(openIdx + 11)..].Trim();
                var call = TryParseXml(inner) ?? TryParseJson(inner);
                if (call != null) results.Add(call);
                return results;
            }
        }

        foreach (Match block in blocks)
        {
            var inner = block.Groups[1].Value.Trim();
            var call = TryParseXml(inner) ?? TryParseJson(inner);
            if (call != null) results.Add(call);
        }

        return results;
    }

    private static ToolCall? TryParseXml(string inner)
    {
        var funcMatch = FunctionRegex().Match(inner);
        if (!funcMatch.Success) return null;

        var name = funcMatch.Groups[1].Value;
        var body = funcMatch.Groups[2].Value;
        var args = new JsonObject();

        foreach (Match param in ParameterRegex().Matches(body))
        {
            var paramName = param.Groups[1].Value;
            var paramValue = param.Groups[2].Value.Trim();
            args[paramName] = paramValue;
        }

        return new ToolCall(name, args);
    }

    public static string GetTextBeforeToolCalls(string text)
    {
        int idx = text.IndexOf("<tool_call>", StringComparison.Ordinal);
        return idx < 0 ? text : text[..idx].TrimEnd();
    }

    private static ToolCall? TryParseJson(string json)
    {
        // Delegate to the llogos JSON parser for backward compatibility
        try
        {
            var calls = Daisi.Llogos.Chat.ToolCallParser.Parse($"<tool_call>{json}</tool_call>");
            return calls.Count > 0 ? calls[0] : null;
        }
        catch { return null; }
    }

    [GeneratedRegex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(@"<function=(\w+)>(.*?)</function>", RegexOptions.Singleline)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"<parameter=(\w+)>\n?(.*?)\n?</parameter>", RegexOptions.Singleline)]
    private static partial Regex ParameterRegex();
}
