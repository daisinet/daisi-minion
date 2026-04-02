using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Daisi.Minion.Config;

/// <summary>
/// Per-model chat harness — defines how to format prompts, inject tools, and detect
/// stop sequences for a specific GGUF model.
///
/// Resolution order:
///   1. User override at ~/.daisi-minion/models/{model}.harness.json
///   2. Built-in harness embedded in the assembly (matched by architecture name)
///   3. Auto-detected from the GGUF tokenizer.chat_template field
///   4. Default (ChatML)
///
/// On first load, the resolved harness is saved to the user directory so it can be
/// edited to fine-tune prompt formatting per model.
/// </summary>
public sealed class ChatHarness
{
    /// <summary>
    /// Chat format: "chatml", "llama3", "gemma", "phi3", or "custom".
    /// Known formats use built-in rendering logic.
    /// "custom" uses the explicit prefix/suffix fields below.
    /// </summary>
    [JsonPropertyName("chat_format")]
    public string ChatFormat { get; set; } = "chatml";

    /// <summary>Whether the model natively supports a dedicated system role.</summary>
    [JsonPropertyName("supports_system_role")]
    public bool SupportsSystemRole { get; set; } = true;

    /// <summary>Whether to prepend BOS token to the start of the prompt.</summary>
    [JsonPropertyName("prepend_bos")]
    public bool PrependBos { get; set; }

    // ── Custom format fields (used when ChatFormat = "custom") ──────────

    [JsonPropertyName("system_prefix")]
    public string? SystemPrefix { get; set; }

    [JsonPropertyName("system_suffix")]
    public string? SystemSuffix { get; set; }

    [JsonPropertyName("user_prefix")]
    public string? UserPrefix { get; set; }

    [JsonPropertyName("user_suffix")]
    public string? UserSuffix { get; set; }

    [JsonPropertyName("assistant_prefix")]
    public string? AssistantPrefix { get; set; }

    [JsonPropertyName("assistant_suffix")]
    public string? AssistantSuffix { get; set; }

    /// <summary>Text prepended to start the assistant's generation turn.</summary>
    [JsonPropertyName("generation_prompt")]
    public string? GenerationPrompt { get; set; }

    // ── Stop sequences ──────────────────────────────────────────────────

    /// <summary>
    /// Strings that signal end-of-turn during generation.
    /// Auto-detected from chat format, or overridden per model.
    /// </summary>
    [JsonPropertyName("stop_sequences")]
    public string[] StopSequences { get; set; } = ["<|im_end|>"];

    // ── Tool calling ────────────────────────────────────────────────────

    /// <summary>
    /// How tool calls appear in model output:
    /// "json_tags" — JSON inside &lt;tool_call&gt; tags (default, works with Qwen/ChatML models)
    /// "raw_json" — bare JSON object (grammar-constrained mode)
    /// "none" — model doesn't support structured tool calling
    /// </summary>
    [JsonPropertyName("tool_call_style")]
    public string ToolCallStyle { get; set; } = "json_tags";

    /// <summary>
    /// Custom instruction text telling the model how to use tools.
    /// When null, uses the default instruction for the tool_call_style.
    /// Set to "" to suppress tool instructions entirely.
    /// </summary>
    [JsonPropertyName("tool_instruction")]
    public string? ToolInstruction { get; set; }

    // ── Resolution ──────────────────────────────────────────────────────

    private static readonly string HarnessDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "models");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Resolve the chat harness for a model. Checks in order:
    ///   1. User override file (~/.daisi-minion/models/{model}.harness.json)
    ///   2. Built-in harness embedded in the assembly (matched by architecture)
    ///   3. Auto-detected from the raw GGUF chat template string
    ///   4. Default (ChatML)
    ///
    /// When resolved from built-in or auto-detection, saves to the user directory
    /// so it can be customized.
    /// </summary>
    /// <param name="modelFilePath">Path to the GGUF file.</param>
    /// <param name="architecture">The model architecture string from GGUF metadata (e.g. "qwen", "bitnet-b1.58").</param>
    /// <param name="rawChatTemplate">The raw tokenizer.chat_template string from GGUF metadata.</param>
    public static ChatHarness Resolve(string modelFilePath, string? architecture, string? rawChatTemplate)
    {
        // 1. User override
        var userHarness = LoadFromUser(modelFilePath);
        if (userHarness != null)
            return userHarness;

        // 2. Built-in by architecture name
        var builtIn = LoadBuiltIn(architecture);
        if (builtIn != null)
        {
            builtIn.Save(modelFilePath);
            return builtIn;
        }

        // 3. Auto-detect from GGUF chat template
        var detected = FromChatTemplate(rawChatTemplate);
        detected.Save(modelFilePath);
        return detected;
    }

    // ── User file persistence ───────────────────────────────────────────

    /// <summary>Load a user-overridden harness, or null if none exists.</summary>
    public static ChatHarness? LoadFromUser(string modelFilePath)
    {
        var path = GetHarnessPath(modelFilePath);
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ChatHarness>(json, JsonOpts);
    }

    /// <summary>Save this harness to the user's model directory.</summary>
    public void Save(string modelFilePath)
    {
        Directory.CreateDirectory(HarnessDir);
        File.WriteAllText(GetHarnessPath(modelFilePath), JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>Check if a user harness file exists for the given model.</summary>
    public static bool Exists(string modelFilePath) => File.Exists(GetHarnessPath(modelFilePath));

    private static string GetHarnessPath(string modelFilePath)
    {
        var modelName = Path.GetFileNameWithoutExtension(modelFilePath);
        return Path.Combine(HarnessDir, $"{modelName}.harness.json");
    }

    // ── Embedded resource loading ───────────────────────────────────────

    /// <summary>
    /// Load a built-in harness from the assembly's embedded resources.
    /// Matches by architecture name: "qwen" → chatml, "bitnet*" → bitnet, etc.
    /// </summary>
    public static ChatHarness? LoadBuiltIn(string? architecture)
    {
        if (string.IsNullOrEmpty(architecture)) return null;

        var resourceName = MapArchitectureToResource(architecture);
        if (resourceName == null) return null;

        var assembly = Assembly.GetExecutingAssembly();
        var fullName = $"Daisi.Minion.Config.Harnesses.{resourceName}.harness.json";

        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream == null) return null;

        return JsonSerializer.Deserialize<ChatHarness>(stream, JsonOpts);
    }

    /// <summary>
    /// Map a GGUF architecture string to a built-in harness resource name.
    /// </summary>
    private static string? MapArchitectureToResource(string architecture)
    {
        var arch = architecture.ToLowerInvariant();

        // BitNet variants
        if (arch.StartsWith("bitnet"))
            return "bitnet";

        // ChatML family (Qwen, Yi, InternLM, etc.)
        if (arch.StartsWith("qwen") || arch == "yi" || arch == "internlm" || arch == "internlm2")
            return "chatml";

        // Llama 3 family
        if (arch == "llama" || arch.StartsWith("llama3"))
            return "llama3";

        // Gemma family
        if (arch.StartsWith("gemma"))
            return "gemma";

        // Phi family
        if (arch.StartsWith("phi"))
            return "phi3";

        return null;
    }

    // ── Auto-detection from GGUF chat template ──────────────────────────

    /// <summary>
    /// Auto-detect a chat harness from a raw GGUF tokenizer.chat_template string.
    /// Falls back to parsing prefix/suffix patterns for unknown templates.
    /// </summary>
    public static ChatHarness FromChatTemplate(string? rawTemplate)
    {
        if (string.IsNullOrEmpty(rawTemplate))
            return Default();

        if (rawTemplate.Contains("<|im_start|>"))
            return ForChatML();

        if (rawTemplate.Contains("<|start_header_id|>"))
            return ForLlama3();

        if (rawTemplate.Contains("<start_of_turn>"))
            return ForGemma();

        if (rawTemplate.Contains("<|user|>") && rawTemplate.Contains("<|end|>"))
            return ForPhi3();

        return ParseCustomTemplate(rawTemplate);
    }

    // ── Factory methods ─────────────────────────────────────────────────

    public static ChatHarness Default() => ForChatML();

    public static ChatHarness ForChatML() => new()
    {
        ChatFormat = "chatml",
        SupportsSystemRole = true,
        StopSequences = ["<|im_end|>"],
        ToolCallStyle = "json_tags",
    };

    public static ChatHarness ForLlama3() => new()
    {
        ChatFormat = "llama3",
        SupportsSystemRole = true,
        PrependBos = true,
        StopSequences = ["<|eot_id|>"],
        ToolCallStyle = "json_tags",
    };

    public static ChatHarness ForGemma() => new()
    {
        ChatFormat = "gemma",
        SupportsSystemRole = false,
        StopSequences = ["<end_of_turn>"],
        ToolCallStyle = "json_tags",
    };

    public static ChatHarness ForPhi3() => new()
    {
        ChatFormat = "phi3",
        SupportsSystemRole = true,
        StopSequences = ["<|end|>"],
        ToolCallStyle = "json_tags",
    };

    // ── Jinja template parsing ──────────────────────────────────────────

    /// <summary>
    /// Parse an unknown Jinja chat template to extract prefix/suffix patterns.
    /// Produces a "custom" harness with best-effort field extraction.
    /// </summary>
    private static ChatHarness ParseCustomTemplate(string template)
    {
        var harness = new ChatHarness
        {
            ChatFormat = "custom",
            SupportsSystemRole = template.Contains("'system'") || template.Contains("\"system\""),
            PrependBos = template.Contains("bos_token"),
            ToolCallStyle = "json_tags",
        };

        // Extract prefix patterns from Jinja string concatenations.
        // Common pattern: 'PrefixText' + message['content']
        var prefixPattern = new Regex(@"'([^']*?)'\s*\+\s*message\['content'\]");
        foreach (Match m in prefixPattern.Matches(template))
        {
            var prefix = m.Groups[1].Value;
            var before = template[..m.Index];
            var lastRole = FindLastRole(before);

            switch (lastRole)
            {
                case "user": harness.UserPrefix ??= prefix; break;
                case "assistant": harness.AssistantPrefix ??= prefix; break;
                case "system": harness.SystemPrefix ??= prefix; break;
            }
        }

        // Extract suffix patterns: message['content'] + 'SuffixText'
        var suffixPattern = new Regex(@"message\['content'\]\s*\+\s*'([^']*?)'");
        foreach (Match m in suffixPattern.Matches(template))
        {
            var suffix = m.Groups[1].Value;
            var before = template[..m.Index];
            var lastRole = FindLastRole(before);

            switch (lastRole)
            {
                case "assistant": harness.AssistantSuffix ??= suffix; break;
                case "system": harness.SystemSuffix ??= suffix; break;
            }
        }

        // Look for compound patterns: message['content'] + '\n\nAssistantName: '
        // This captures user suffix + assistant prefix as one string after user content
        var compoundSuffix = new Regex(@"message\['content'\]\s*\+\s*'([^']*?\\n[^']*?)'");
        foreach (Match m in compoundSuffix.Matches(template))
        {
            var raw = Unescape(m.Groups[1].Value);
            var before = template[..m.Index];
            if (FindLastRole(before) != "user") continue;

            // Split on newlines: leading newlines → user suffix, trailing text → assistant prefix
            var parts = raw.Split('\n', StringSplitOptions.None);
            var lastNonEmpty = Array.FindLastIndex(parts, p => p.Length > 0);
            if (lastNonEmpty > 0)
            {
                harness.UserSuffix = string.Join('\n', parts[..lastNonEmpty]) + "\n";
                harness.AssistantPrefix = parts[lastNonEmpty];
                harness.GenerationPrompt = harness.AssistantPrefix;
            }
        }

        // Apply defaults for anything we couldn't parse
        harness.UserPrefix ??= "";
        harness.AssistantPrefix ??= "";
        harness.UserSuffix ??= "\n";
        harness.AssistantSuffix ??= "\n";
        harness.GenerationPrompt ??= harness.AssistantPrefix;

        // Stop on user prefix (next turn)
        var stops = new List<string>();
        if (!string.IsNullOrWhiteSpace(harness.UserPrefix))
            stops.Add(harness.UserPrefix.TrimEnd());
        harness.StopSequences = stops.Count > 0 ? [.. stops] : ["</s>"];

        return harness;
    }

    private static string? FindLastRole(string text)
    {
        var u = text.LastIndexOf("'user'", StringComparison.Ordinal);
        var a = text.LastIndexOf("'assistant'", StringComparison.Ordinal);
        var s = text.LastIndexOf("'system'", StringComparison.Ordinal);
        var max = Math.Max(u, Math.Max(a, s));
        if (max < 0) return null;
        if (max == u) return "user";
        if (max == a) return "assistant";
        return "system";
    }

    private static string Unescape(string s) =>
        s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\'", "'");
}
