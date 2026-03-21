using System.Text;
using System.Text.Json;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Minion's own ChatML-based chat renderer with full support for:
/// - Tool definitions in system prompt via &lt;tools&gt; block
/// - Tool calls via &lt;tool_call&gt; tags in assistant messages
/// - Tool results via &lt;tool_response&gt; blocks inside user messages
/// - Thinking via &lt;think&gt; tags in assistant messages and generation prompt
/// - Multi-step tool call reasoning
///
/// This is used instead of the GGUF-embedded template so the minion controls
/// the exact prompt format regardless of which model is loaded.
/// </summary>
public sealed class MinionChatRenderer : IChatRenderer
{
    private readonly IReadOnlyList<ToolDefinition> _tools;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public MinionChatRenderer(IReadOnlyList<ToolDefinition> tools)
    {
        _tools = tools;
    }

    public string[] GetStopSequences() => ["<|im_end|>", "</tool_call>", "</think>", "</thinking>"];

    public string Render(IReadOnlyList<ChatMessage> messages, bool addGenerationPrompt = true)
    {
        var sb = new StringBuilder();

        // --- System message with optional tools block ---
        if (_tools.Count > 0)
        {
            sb.Append("<|im_start|>system\n");

            // Include system message content if first message is system
            if (messages.Count > 0 && messages[0].Role == "system")
                sb.Append(messages[0].Content).Append("\n\n");

            // Tools block
            sb.Append("# Tools\n\nYou may call one or more functions to assist with the user query.\n\n");
            sb.Append("You are provided with function signatures within <tools></tools> XML tags:\n<tools>");
            foreach (var tool in _tools)
            {
                sb.Append('\n');
                var toolObj = new
                {
                    type = "function",
                    function = new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        parameters = tool.ParametersSchema,
                    }
                };
                sb.Append(JsonSerializer.Serialize(toolObj, JsonOpts));
            }
            sb.Append("\n</tools>\n\n");
            sb.Append("For each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\n");
            sb.Append("<tool_call>\n{\"name\": <function-name>, \"arguments\": <args-json-object>}\n</tool_call>");
            sb.Append("<|im_end|>\n");
        }
        else
        {
            // No tools — render system message normally if present
            if (messages.Count > 0 && messages[0].Role == "system")
                sb.Append("<|im_start|>system\n").Append(messages[0].Content).Append("<|im_end|>\n");
        }

        // --- Conversation messages ---
        // Find last real user query index (not a tool response)
        int lastQueryIndex = FindLastUserQueryIndex(messages);

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            // Skip the first system message (already rendered above)
            if (i == 0 && msg.Role == "system")
                continue;

            switch (msg.Role)
            {
                case "user":
                    sb.Append("<|im_start|>user\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "system":
                    // Non-first system messages rendered as system turns
                    sb.Append("<|im_start|>system\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "assistant":
                    RenderAssistantMessage(sb, msg, i, lastQueryIndex, messages, addGenerationPrompt && i == messages.Count - 1);
                    break;

                case "tool":
                    RenderToolResponse(sb, msg, i, messages);
                    break;
            }
        }

        // --- Generation prompt ---
        if (addGenerationPrompt)
        {
            // If the last message was already an assistant message being continued, don't add another
            if (messages.Count == 0 || messages[^1].Role != "assistant")
            {
                // Qwen 3.5 non-thinking format: empty think block signals
                // "thinking is done, respond directly." Without this, the model
                // opens <think> and never closes it.
                sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");
            }
        }

        return sb.ToString();
    }

    private static void RenderAssistantMessage(StringBuilder sb, ChatMessage msg, int index,
        int lastQueryIndex, IReadOnlyList<ChatMessage> messages, bool isLast)
    {
        var content = msg.Content ?? "";

        // Extract thinking content if present
        string thinkContent = "";
        string mainContent = content;

        if (content.Contains("</think>"))
        {
            var thinkEnd = content.IndexOf("</think>", StringComparison.Ordinal);
            var thinkStart = content.IndexOf("<think>", StringComparison.Ordinal);
            if (thinkStart >= 0)
                thinkContent = content[(thinkStart + 7)..thinkEnd].Trim();
            else
                thinkContent = content[..thinkEnd].Trim();
            mainContent = content[(thinkEnd + 8)..].TrimStart();
        }

        // For messages after the last user query in multi-step tool scenarios,
        // include thinking tags
        if (index > lastQueryIndex && (isLast || thinkContent.Length > 0))
            sb.Append("<|im_start|>assistant\n<think>\n").Append(thinkContent).Append("\n</think>\n\n").Append(mainContent);
        else
            sb.Append("<|im_start|>assistant\n").Append(mainContent);

        sb.Append("<|im_end|>\n");
    }

    private static void RenderToolResponse(StringBuilder sb, ChatMessage msg, int index,
        IReadOnlyList<ChatMessage> messages)
    {
        // Tool responses are wrapped in <tool_response> inside a user message
        // Multiple consecutive tool results are grouped into one user turn
        bool isFirstToolInGroup = index == 0 || messages[index - 1].Role != "tool";
        bool isLastToolInGroup = index == messages.Count - 1 || messages[index + 1].Role != "tool";

        if (isFirstToolInGroup)
            sb.Append("<|im_start|>user");

        sb.Append("\n<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");

        if (isLastToolInGroup)
            sb.Append("<|im_end|>\n");
    }

    /// <summary>
    /// Find the index of the last user message that is NOT a tool response.
    /// Used for multi-step tool call logic.
    /// </summary>
    private static int FindLastUserQueryIndex(IReadOnlyList<ChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == "user"
                && !messages[i].Content.StartsWith("<tool_response>"))
                return i;
        }
        return messages.Count - 1;
    }
}
