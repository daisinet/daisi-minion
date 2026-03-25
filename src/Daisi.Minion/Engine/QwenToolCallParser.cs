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
        // Strip leading/trailing whitespace and any trailing incomplete tags
        json = json.Trim();
        if (json.Length == 0) return null;

        // Find the JSON object boundaries
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        var jsonStr = json[start..(end + 1)];

        // Fix common Qwen malformations
        jsonStr = FixMalformedJson(jsonStr);

        // Try direct JSON parsing
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            string? name = null;
            var args = new JsonObject();

            if (root.TryGetProperty("name", out var nameProp))
                name = nameProp.GetString();

            if (root.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        System.Text.Json.JsonValueKind.String => JsonValue.Create(prop.Value.GetString()),
                        System.Text.Json.JsonValueKind.Number => JsonValue.Create(prop.Value.GetInt32()),
                        System.Text.Json.JsonValueKind.True => JsonValue.Create(true),
                        System.Text.Json.JsonValueKind.False => JsonValue.Create(false),
                        _ => JsonNode.Parse(prop.Value.GetRawText()),
                    };
                }
            }

            if (name != null)
                return new ToolCall(name, args);
        }
        catch { }

        // Fallback to llogos parser
        try
        {
            var calls = Daisi.Llogos.Chat.ToolCallParser.Parse($"<tool_call>{json}</tool_call>");
            return calls.Count > 0 ? calls[0] : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Fix common JSON malformations from the Qwen 3.5 model:
    /// - Missing opening quote on keys: arguments": → "arguments":
    /// - Trailing commas before closing braces
    /// </summary>
    private static string FixMalformedJson(string json)
    {
        // Fix specific known malformations from Qwen 3.5:
        // The model sometimes drops the opening " on JSON keys, producing:
        //   {"name": "file_edit", arguments": {"path": "..."}}
        //                         ^ missing opening quote
        // Fix by finding , or { followed by a space and an unquoted key
        json = UnquotedKeyRegex().Replace(json, "$1\"$2\":");

        // Fix: trailing commas before }
        json = TrailingCommaRegex().Replace(json, "}");

        return json;
    }

    // Match: comma or brace, optional whitespace, unquoted word, optional ", colon
    // Captures: the separator ($1) and the key ($2)
    [GeneratedRegex(@"([,{])\s*(\w+)""?\s*:", RegexOptions.None)]
    private static partial Regex UnquotedKeyRegex();

    [GeneratedRegex(@",\s*\}", RegexOptions.None)]
    private static partial Regex TrailingCommaRegex();

    [GeneratedRegex(@"<tool_call>\s*(.*?)\s*</tool_call>", RegexOptions.Singleline)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(@"<function=(\w+)>(.*?)</function>", RegexOptions.Singleline)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"<parameter=(\w+)>\n?(.*?)\n?</parameter>", RegexOptions.Singleline)]
    private static partial Regex ParameterRegex();
}
