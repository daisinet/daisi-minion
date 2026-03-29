using System.Text;
using System.Text.Json;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// ChatML renderer with JSON tool calls — matching the model's native training format.
/// Tool definitions in system prompt, JSON tool calls in assistant output,
/// tool results wrapped in <tool_response> blocks.
/// </summary>
public sealed class MinionChatRenderer : IChatRenderer
{
    private readonly IReadOnlyList<ToolDefinition> _tools;
    private readonly bool _grammarMode;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <param name="tools">Available tool definitions.</param>
    /// <param name="grammarMode">When true, omits the tool-call instruction preamble
    /// and the &lt;/tool_call&gt; stop sequence — the grammar handles structure instead.</param>
    public MinionChatRenderer(IReadOnlyList<ToolDefinition> tools, bool grammarMode = false)
    {
        _tools = tools;
        _grammarMode = grammarMode;
    }

    public string[] GetStopSequences() => _grammarMode
        ? ["<|im_end|>"]
        : ["<|im_end|>", "</tool_call>"];

    public string Render(IReadOnlyList<ChatMessage> messages, bool addGenerationPrompt = true)
    {
        var sb = new StringBuilder();

        // --- System message with tools ---
        bool hasSystemMessage = messages.Count > 0 && messages[0].Role == "system";
        string? systemContent = hasSystemMessage ? messages[0].Content : null;

        if (_tools.Count > 0)
        {
            sb.Append("<|im_start|>system\n");

            // System content before tools
            if (systemContent != null)
                sb.Append(systemContent).Append('\n');

            // Tools block
            sb.Append("\n<tools>\n");
            foreach (var tool in _tools)
            {
                sb.Append(JsonSerializer.Serialize(new
                {
                    type = "function",
                    function = new { name = tool.Name, description = tool.Description, parameters = tool.ParametersSchema }
                }, JsonOpts));
                sb.Append('\n');
            }
            sb.Append("</tools>\n\n");

            if (_grammarMode)
            {
                sb.Append("IMPORTANT: Do not use <think> or <response> tags. Respond with raw JSON only.\n");
                sb.Append("When asked to create, edit, or build something, respond with the tool call JSON immediately.\n");
            }
            else
            {
                sb.Append("To call a function, respond with a JSON object inside <tool_call> tags:\n\n");
                sb.Append("<tool_call>\n{\"name\": \"function_name\", \"arguments\": {\"param\": \"value\"}}\n</tool_call>\n\n");
                sb.Append("When asked to create, edit, or build something, respond with a tool_call immediately.\n");
                sb.Append("Do not describe what you plan to do — call the tool.\n");
            }

            sb.Append("<|im_end|>\n");
        }
        else
        {
            if (hasSystemMessage)
                sb.Append("<|im_start|>system\n").Append(systemContent).Append("<|im_end|>\n");
        }

        // --- Conversation messages ---
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == 0 && msg.Role == "system") continue;

            switch (msg.Role)
            {
                case "user":
                    if (!string.IsNullOrEmpty(msg.Content))
                        sb.Append("<|im_start|>user\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "system":
                    sb.Append("<|im_start|>system\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "assistant":
                    RenderAssistantMessage(sb, msg);
                    break;

                case "tool":
                    RenderToolResponse(sb, msg, i, messages);
                    break;
            }
        }

        // --- Generation prompt ---
        if (addGenerationPrompt)
        {
            if (messages.Count == 0 || messages[^1].Role != "assistant")
            {
                sb.Append("<|im_start|>assistant\n");
            }
        }

        return sb.ToString();
    }

    private static void RenderAssistantMessage(StringBuilder sb, ChatMessage msg)
    {
        var content = msg.Content ?? "";
        sb.Append("<|im_start|>assistant\n").Append(content).Append("<|im_end|>\n");
    }

    private static void RenderToolResponse(StringBuilder sb, ChatMessage msg, int index,
        IReadOnlyList<ChatMessage> messages)
    {
        bool isFirstToolInGroup = index == 0 || messages[index - 1].Role != "tool";
        bool isLastToolInGroup = index == messages.Count - 1 || messages[index + 1].Role != "tool";

        if (isFirstToolInGroup)
            sb.Append("<|im_start|>user");

        sb.Append("\n<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");

        if (isLastToolInGroup)
            sb.Append("<|im_end|>\n");
    }
}
