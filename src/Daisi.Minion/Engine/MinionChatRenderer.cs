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

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public MinionChatRenderer(IReadOnlyList<ToolDefinition> tools)
    {
        _tools = tools;
    }

    public string[] GetStopSequences() => ["<|im_end|>", "</tool_call>"];

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

            sb.Append("To call a function, respond with a JSON object inside <tool_call> tags:\n\n");
            sb.Append("<tool_call>\n{\"name\": \"function_name\", \"arguments\": {\"param\": \"value\"}}\n</tool_call>\n\n");
            sb.Append("When asked to create, edit, or build something, respond with a tool_call immediately.\n");
            sb.Append("Do not describe what you plan to do — call the tool.\n");

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
                sb.Append("<|im_start|>assistant\n<think>\n");
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
