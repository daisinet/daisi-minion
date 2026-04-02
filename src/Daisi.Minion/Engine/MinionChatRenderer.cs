using System.Text;
using System.Text.Json;
using Daisi.Llogos.Chat;
using Daisi.Minion.Config;

namespace Daisi.Minion.Engine;

/// <summary>
/// Chat renderer that formats prompts according to a per-model <see cref="ChatHarness"/>.
/// Injects tool definitions into the system prompt and handles tool response wrapping
/// across all supported chat formats (ChatML, Llama3, Gemma, Phi3, custom).
///
/// When no harness is provided, defaults to ChatML for backward compatibility.
/// </summary>
public sealed class MinionChatRenderer : IChatRenderer
{
    private readonly IReadOnlyList<ToolDefinition> _tools;
    private readonly bool _grammarMode;
    private readonly ChatHarness _harness;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <param name="tools">Available tool definitions.</param>
    /// <param name="grammarMode">When true, omits the tool-call instruction preamble
    /// and the &lt;/tool_call&gt; stop sequence — the grammar handles structure instead.</param>
    /// <param name="harness">Per-model chat harness. Defaults to ChatML when null.</param>
    public MinionChatRenderer(IReadOnlyList<ToolDefinition> tools, bool grammarMode = false, ChatHarness? harness = null)
    {
        _tools = tools;
        _grammarMode = grammarMode;
        _harness = harness ?? ChatHarness.Default();
    }

    public string[] GetStopSequences()
    {
        var stops = new List<string>(_harness.StopSequences);
        if (!_grammarMode && _harness.ToolCallStyle == "json_tags" && !stops.Contains("</tool_call>"))
            stops.Add("</tool_call>");
        return [.. stops];
    }

    public string Render(IReadOnlyList<ChatMessage> messages, bool addGenerationPrompt = true)
    {
        return _harness.ChatFormat switch
        {
            "chatml" => RenderChatML(messages, addGenerationPrompt),
            "llama3" => RenderLlama3(messages, addGenerationPrompt),
            "gemma" => RenderGemma(messages, addGenerationPrompt),
            "phi3" => RenderPhi3(messages, addGenerationPrompt),
            "custom" => RenderCustom(messages, addGenerationPrompt),
            _ => RenderChatML(messages, addGenerationPrompt),
        };
    }

    // ── Tool definition block (format-agnostic) ─────────────────────────

    private string BuildToolsBlock()
    {
        if (_tools.Count == 0) return "";

        // Serialize tool definitions as JSON lines
        var toolJson = new StringBuilder();
        foreach (var tool in _tools)
        {
            toolJson.Append(JsonSerializer.Serialize(new
            {
                type = "function",
                function = new { name = tool.Name, description = tool.Description, parameters = tool.ParametersSchema }
            }, JsonOpts));
            toolJson.Append('\n');
        }
        var toolDefs = toolJson.ToString().TrimEnd();

        // If the harness has a tool_instruction with {tool_definitions} placeholder,
        // use it as a complete template (e.g. Qwen3's "# Tools\n\nYou may call..." format)
        if (!_grammarMode && _harness.ToolInstruction != null
            && _harness.ToolInstruction.Contains("{tool_definitions}"))
        {
            return "\n" + _harness.ToolInstruction.Replace("{tool_definitions}", toolDefs) + "\n";
        }

        // Otherwise build the block with <tools> wrapper + instruction text
        var sb = new StringBuilder();
        sb.Append("\n<tools>\n").Append(toolDefs).Append("\n</tools>\n\n");

        if (_grammarMode)
        {
            sb.Append("IMPORTANT: Do not use <think> or <response> tags. Respond with raw JSON only.\n");
            sb.Append("When asked to create, edit, or build something, respond with the tool call JSON immediately.\n");
        }
        else if (_harness.ToolInstruction != null)
        {
            if (_harness.ToolInstruction.Length > 0)
                sb.Append(_harness.ToolInstruction).Append('\n');
        }
        else
        {
            sb.Append("To call a function, respond with a JSON object inside <tool_call> tags:\n\n");
            sb.Append("<tool_call>\n{\"name\": \"function_name\", \"arguments\": {\"param\": \"value\"}}\n</tool_call>\n\n");
            sb.Append("When asked to create, edit, or build something, respond with a tool_call immediately.\n");
            sb.Append("Do not describe what you plan to do — call the tool.\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build the full system content: base system prompt + tool definitions.
    /// </summary>
    private string BuildSystemContent(string? systemContent)
    {
        var sb = new StringBuilder();
        if (systemContent != null)
            sb.Append(systemContent);

        var toolsBlock = BuildToolsBlock();
        if (toolsBlock.Length > 0 && systemContent != null)
            sb.Append('\n'); // separate system content from tools block
        sb.Append(toolsBlock);
        return sb.ToString();
    }

    // ── ChatML ──────────────────────────────────────────────────────────
    // <|im_start|>role\ncontent<|im_end|>\n

    private string RenderChatML(IReadOnlyList<ChatMessage> messages, bool addGenPrompt)
    {
        var sb = new StringBuilder();
        bool hasSystem = messages.Count > 0 && messages[0].Role == "system";

        if (_tools.Count > 0)
        {
            sb.Append("<|im_start|>system\n");
            sb.Append(BuildSystemContent(hasSystem ? messages[0].Content : null));
            sb.Append("<|im_end|>\n");
        }
        else if (hasSystem)
        {
            sb.Append("<|im_start|>system\n").Append(messages[0].Content).Append("<|im_end|>\n");
        }

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
                    sb.Append("<|im_start|>assistant\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;
                case "tool":
                    RenderToolResponseChatML(sb, msg, i, messages);
                    break;
            }
        }

        if (addGenPrompt && (messages.Count == 0 || messages[^1].Role != "assistant"))
            sb.Append("<|im_start|>assistant\n");

        return sb.ToString();
    }

    private static void RenderToolResponseChatML(StringBuilder sb, ChatMessage msg, int index,
        IReadOnlyList<ChatMessage> messages)
    {
        bool isFirst = index == 0 || messages[index - 1].Role != "tool";
        bool isLast = index == messages.Count - 1 || messages[index + 1].Role != "tool";

        if (isFirst) sb.Append("<|im_start|>user");
        sb.Append("\n<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
        if (isLast) sb.Append("<|im_end|>\n");
    }

    // ── Llama 3 ─────────────────────────────────────────────────────────
    // <|start_header_id|>role<|end_header_id|>\n\ncontent<|eot_id|>

    private string RenderLlama3(IReadOnlyList<ChatMessage> messages, bool addGenPrompt)
    {
        var sb = new StringBuilder();
        sb.Append("<|begin_of_text|>");

        bool hasSystem = messages.Count > 0 && messages[0].Role == "system";

        if (_tools.Count > 0)
        {
            sb.Append("<|start_header_id|>system<|end_header_id|>\n\n");
            sb.Append(BuildSystemContent(hasSystem ? messages[0].Content : null));
            sb.Append("<|eot_id|>");
        }
        else if (hasSystem)
        {
            sb.Append("<|start_header_id|>system<|end_header_id|>\n\n");
            sb.Append(messages[0].Content).Append("<|eot_id|>");
        }

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == 0 && msg.Role == "system") continue;

            switch (msg.Role)
            {
                case "user":
                case "system":
                    sb.Append($"<|start_header_id|>{msg.Role}<|end_header_id|>\n\n");
                    sb.Append(msg.Content).Append("<|eot_id|>");
                    break;
                case "assistant":
                    sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
                    sb.Append(msg.Content).Append("<|eot_id|>");
                    break;
                case "tool":
                    sb.Append("<|start_header_id|>tool<|end_header_id|>\n\n");
                    sb.Append("<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
                    sb.Append("<|eot_id|>");
                    break;
            }
        }

        if (addGenPrompt)
            sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");

        return sb.ToString();
    }

    // ── Gemma ───────────────────────────────────────────────────────────
    // <start_of_turn>role\ncontent<end_of_turn>\n

    private string RenderGemma(IReadOnlyList<ChatMessage> messages, bool addGenPrompt)
    {
        var sb = new StringBuilder();
        bool hasSystem = messages.Count > 0 && messages[0].Role == "system";
        string? deferredSystem = null;

        // Gemma has no system role — prepend to first user message
        if (hasSystem)
            deferredSystem = BuildSystemContent(messages[0].Content);
        else if (_tools.Count > 0)
            deferredSystem = BuildSystemContent(null);

        bool systemInjected = false;

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == 0 && msg.Role == "system") continue;

            var role = msg.Role == "assistant" ? "model" : msg.Role;

            if (msg.Role == "user" && !systemInjected && deferredSystem != null)
            {
                sb.Append("<start_of_turn>user\n");
                sb.Append(deferredSystem).Append("\n\n").Append(msg.Content);
                sb.Append("<end_of_turn>\n");
                systemInjected = true;
            }
            else if (msg.Role == "tool")
            {
                sb.Append("<start_of_turn>user\n");
                sb.Append("<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
                sb.Append("<end_of_turn>\n");
            }
            else
            {
                sb.Append($"<start_of_turn>{role}\n").Append(msg.Content).Append("<end_of_turn>\n");
            }
        }

        if (addGenPrompt)
            sb.Append("<start_of_turn>model\n");

        return sb.ToString();
    }

    // ── Phi-3 ───────────────────────────────────────────────────────────
    // <|user|>\ncontent<|end|>\n<|assistant|>\ncontent<|end|>\n

    private string RenderPhi3(IReadOnlyList<ChatMessage> messages, bool addGenPrompt)
    {
        var sb = new StringBuilder();
        bool hasSystem = messages.Count > 0 && messages[0].Role == "system";

        if (_tools.Count > 0)
        {
            sb.Append("<|system|>\n");
            sb.Append(BuildSystemContent(hasSystem ? messages[0].Content : null));
            sb.Append("<|end|>\n");
        }
        else if (hasSystem)
        {
            sb.Append("<|system|>\n").Append(messages[0].Content).Append("<|end|>\n");
        }

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == 0 && msg.Role == "system") continue;

            sb.Append("<|").Append(msg.Role).Append("|>\n");
            if (msg.Role == "tool")
                sb.Append("<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
            else
                sb.Append(msg.Content);
            sb.Append("<|end|>\n");
        }

        if (addGenPrompt)
            sb.Append("<|assistant|>\n");

        return sb.ToString();
    }

    // ── Custom format ───────────────────────────────────────────────────
    // Uses explicit prefix/suffix fields from the harness.

    private string RenderCustom(IReadOnlyList<ChatMessage> messages, bool addGenPrompt)
    {
        var sb = new StringBuilder();
        bool hasSystem = messages.Count > 0 && messages[0].Role == "system";

        string? deferredSystem = null;

        if (hasSystem && _harness.SupportsSystemRole)
        {
            // Render system message using system prefix/suffix
            sb.Append(_harness.SystemPrefix ?? "");
            sb.Append(BuildSystemContent(messages[0].Content));
            sb.Append(_harness.SystemSuffix ?? "\n");
        }
        else if (hasSystem)
        {
            // No system role — defer to first user message
            deferredSystem = BuildSystemContent(messages[0].Content);
        }
        else if (_tools.Count > 0 && !_harness.SupportsSystemRole)
        {
            // No system message but we have tools — defer tool block
            deferredSystem = BuildSystemContent(null);
        }
        else if (_tools.Count > 0)
        {
            sb.Append(_harness.SystemPrefix ?? "");
            sb.Append(BuildSystemContent(null));
            sb.Append(_harness.SystemSuffix ?? "\n");
        }

        bool systemInjected = false;

        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i == 0 && msg.Role == "system") continue;

            switch (msg.Role)
            {
                case "user":
                    sb.Append(_harness.UserPrefix ?? "");
                    if (!systemInjected && deferredSystem != null)
                    {
                        sb.Append(deferredSystem).Append("\n\n");
                        systemInjected = true;
                    }
                    sb.Append(msg.Content);
                    sb.Append(_harness.UserSuffix ?? "\n");
                    break;

                case "assistant":
                    sb.Append(_harness.AssistantPrefix ?? "");
                    sb.Append(msg.Content);
                    sb.Append(_harness.AssistantSuffix ?? "\n");
                    break;

                case "system":
                    if (_harness.SupportsSystemRole)
                    {
                        sb.Append(_harness.SystemPrefix ?? "");
                        sb.Append(msg.Content);
                        sb.Append(_harness.SystemSuffix ?? "\n");
                    }
                    else
                    {
                        // Inject as user message
                        sb.Append(_harness.UserPrefix ?? "");
                        sb.Append(msg.Content);
                        sb.Append(_harness.UserSuffix ?? "\n");
                    }
                    break;

                case "tool":
                    // Tool responses go into a user turn
                    sb.Append(_harness.UserPrefix ?? "");
                    sb.Append("<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
                    sb.Append(_harness.UserSuffix ?? "\n");
                    break;
            }
        }

        if (addGenPrompt && (messages.Count == 0 || messages[^1].Role != "assistant"))
            sb.Append(_harness.GenerationPrompt ?? _harness.AssistantPrefix ?? "");

        return sb.ToString();
    }
}
