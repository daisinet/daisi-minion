using System.Text;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Builds GBNF grammars for constrained tool-call generation.
/// Produces grammars for different strategies to compare quality/reliability.
/// </summary>
public static class ToolCallGrammarBuilder
{
    /// <summary>
    /// Strategy A: Strict — output is exactly a JSON tool call, no preamble.
    /// {"name":"tool_name","arguments":{...}}
    /// </summary>
    public static string BuildStrict(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("root ::= tool-call-json");
        BuildCore(sb, tools);
        return sb.ToString();
    }

    /// <summary>
    /// Strategy B: Thinking + tool call — optional think block, then JSON tool call.
    /// </summary>
    public static string BuildThinkingThenTool(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("root ::= thinking tool-call-json");
        sb.AppendLine("thinking ::= \"<think>\" think-chars \"</think>\" ws");
        sb.AppendLine("think-chars ::= think-char*");
        sb.AppendLine("think-char ::= [^<] | \"<\" [^/] | \"</\" [^t] | \"</t\" [^h] | \"</th\" [^i] | \"</thi\" [^n] | \"</thin\" [^k] | \"</think\" [^>]");
        BuildCore(sb, tools);
        return sb.ToString();
    }

    /// <summary>
    /// Strategy C: JSON tool call wrapped in tool_call tags.
    /// &lt;tool_call&gt;\n{...}\n&lt;/tool_call&gt;
    /// </summary>
    public static string BuildTagWrapped(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("root ::= \"<tool_call>\\n\" tool-call-json \"\\n</tool_call>\"");
        BuildCore(sb, tools);
        return sb.ToString();
    }

    private static void BuildCore(StringBuilder sb, IReadOnlyList<ToolDefinition> tools)
    {
        // Tool call JSON: {"name":"<tool>","arguments":{...}}
        sb.AppendLine("tool-call-json ::= \"{\" ws namekey ws \":\" ws tool-name ws \",\" ws argskey ws \":\" ws tool-args ws \"}\"");
        sb.AppendLine("namekey ::= \"\\\"name\\\"\"");
        sb.AppendLine("argskey ::= \"\\\"arguments\\\"\"");

        // Tool names — enumerate registered tools + synthetic "complete" signal
        var nameAlts = new List<string>();
        foreach (var tool in tools)
            nameAlts.Add($"\"\\\"\" \"{EscapeGbnf(tool.Name)}\" \"\\\"\"");
        nameAlts.Add("\"\\\"\" \"complete\" \"\\\"\"");
        sb.AppendLine($"tool-name ::= {string.Join(" | ", nameAlts)}");

        // Tool arguments — any valid JSON object
        sb.AppendLine("tool-args ::= \"{\" ws arglist? \"}\"");
        sb.AppendLine("arglist ::= argpair (\",\" ws argpair)*");
        sb.AppendLine("argpair ::= jstr ws \":\" ws jval ws");

        // JSON primitives
        sb.AppendLine("jval ::= jstr | jnum | jbool | jnull | jobj | jarr");
        sb.AppendLine("jstr ::= \"\\\"\" jchar* \"\\\"\"");
        sb.AppendLine("jchar ::= [^\"\\\\\\x00-\\x1F] | \"\\\\\" jesc");
        sb.AppendLine("jesc ::= [\"\\\\bfnrt/] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]");
        sb.AppendLine("jnum ::= \"-\"? [0-9]+ (\".\" [0-9]+)? ([eE] [\"+\\-\"]? [0-9]+)?");
        sb.AppendLine("jbool ::= \"true\" | \"false\"");
        sb.AppendLine("jnull ::= \"null\"");
        sb.AppendLine("jobj ::= \"{\" ws jmembers? \"}\"");
        sb.AppendLine("jmembers ::= jmember (\",\" ws jmember)*");
        sb.AppendLine("jmember ::= jstr ws \":\" ws jval ws");
        sb.AppendLine("jarr ::= \"[\" ws jarritems? \"]\"");
        sb.AppendLine("jarritems ::= jval ws (\",\" ws jval ws)*");
        sb.AppendLine("ws ::= [ \\t\\n]*");
    }

    private static string EscapeGbnf(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
