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

    public string[] GetStopSequences() => ["<|im_end|>", "</tool_call>"];

    public string Render(IReadOnlyList<ChatMessage> messages, bool addGenerationPrompt = true)
    {
        var sb = new StringBuilder();

        // --- System message with optional tools block ---
        if (_tools.Count > 0)
        {
            sb.Append("<|im_start|>system\n");

            // System message content goes AFTER tools (matches Qwen native template)
            string? systemContent = null;
            if (messages.Count > 0 && messages[0].Role == "system")
                systemContent = messages[0].Content;

            // Tools block — matches Qwen 3.5 native chat_template.jinja format
            sb.Append("# Tools\n\nYou have access to the following functions:\n\n<tools>");
            foreach (var tool in _tools)
            {
                sb.Append('\n');
                sb.Append(JsonSerializer.Serialize(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.ParametersSchema,
                }, JsonOpts));
            }
            sb.Append("\n</tools>\n\n");
            sb.Append("If you choose to call a function ONLY reply in the following format with NO suffix:\n\n");
            sb.Append("<tool_call>\n<function=example_function_name>\n<parameter=example_parameter>\nvalue\n</parameter>\n</function>\n</tool_call>\n\n");
            sb.Append("Example — to write a file, respond with ONLY:\n\n");
            sb.Append("<tool_call>\n<function=file_write>\n<parameter=path>\nhello.txt\n</parameter>\n<parameter=content>\nHello World\n</parameter>\n</function>\n</tool_call>\n\n");
            sb.Append("<IMPORTANT>\nWhen asked to create, edit, or build something, respond with a tool call. Do not describe what you plan to do — call the tool.\n</IMPORTANT>");
            if (systemContent != null)
                sb.Append("\n\n").Append(systemContent);
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
                // Qwen 3.5 thinking mode (default for unsloth GGUF builds).
                // Open <think> tag lets the model reason before responding or
                // calling tools. StreamByLine strips thinking from display.
                sb.Append("<|im_start|>assistant\n<think>\n");
            }
        }

        return sb.ToString();
    }

    private static void RenderAssistantMessage(StringBuilder sb, ChatMessage msg, int index,
        int lastQueryIndex, IReadOnlyList<ChatMessage> messages, bool isLast)
    {
        var content = msg.Content ?? "";

        // Per Qwen 3.5 docs: "Historical model outputs should only include the
        // final output part without thinking content." Strip <think>...</think>.
        if (content.Contains("</think>"))
        {
            var thinkEnd = content.IndexOf("</think>", StringComparison.Ordinal);
            content = content[(thinkEnd + 8)..].TrimStart();
        }
        else if (content.TrimStart().StartsWith("<think>"))
        {
            // Unclosed think block — strip the tag
            var idx = content.IndexOf("<think>", StringComparison.Ordinal);
            content = content[(idx + 7)..].TrimStart();
        }

        sb.Append("<|im_start|>assistant\n").Append(content).Append("<|im_end|>\n");
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
