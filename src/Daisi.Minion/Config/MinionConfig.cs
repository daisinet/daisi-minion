using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daisi.Minion.Config;

/// <summary>
/// Settings for daisi-minion, stored in ~/.daisi-minion/config.json.
/// </summary>
public sealed class MinionConfig
{
    [JsonPropertyName("models_directory")]
    public string ModelsDirectory { get; set; } = @"C:\GGUFS";

    [JsonPropertyName("active_model")]
    public string? ActiveModel { get; set; }

    [JsonPropertyName("backend")]
    public string Backend { get; set; } = "auto";

    [JsonPropertyName("idle_timeout_minutes")]
    public int IdleTimeoutMinutes { get; set; } = 5;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("context_size")]
    public int ContextSize { get; set; } = 8192;

    [JsonPropertyName("thread_count")]
    public int ThreadCount { get; set; } = Math.Max(1, Environment.ProcessorCount - 2);

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Active role (coder, cto, chat, etc.).</summary>
    [JsonPropertyName("active_role")]
    public string? ActiveRole { get; set; } = "chat";

    /// <summary>Active persona / personality trait (witty, dry, sarcastic, etc.). Null = none.</summary>
    [JsonPropertyName("active_persona")]
    public string? ActivePersona { get; set; }

    [JsonPropertyName("minion_name")]
    public string MinionName { get; set; } = "minion";

    /// <summary>Override working directory. If set, the minion cd's here on startup.</summary>
    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>KV cache compression mode: null (default), "turbo", "turbo:3", "turbo:4", "turbo:3+qjl32".</summary>
    [JsonPropertyName("kv_quant")]
    public string? KvQuant { get; set; }

    /// <summary>Use GBNF grammar to constrain tool call output to valid JSON. Eliminates malformed tool calls.</summary>
    [JsonPropertyName("grammar_tool_calls")]
    public bool UseGrammarToolCalls { get; set; }

    // ── DaisiGit Module Repository ──

    /// <summary>DaisiGit server URL (e.g., https://git.daisi.ai).</summary>
    [JsonPropertyName("daisigit_server")]
    public string? DaisiGitServer { get; set; }

    /// <summary>DaisiGit API key (dg_...) for authenticated access.</summary>
    [JsonPropertyName("daisigit_token")]
    public string? DaisiGitToken { get; set; }

    /// <summary>Modules repo on DaisiGit as "owner/slug" (e.g., "myhandle/daisi-minion-modules").</summary>
    [JsonPropertyName("modules_repo")]
    public string? ModulesRepo { get; set; }

    /// <summary>Branch to pull modules from. Defaults to "main".</summary>
    [JsonPropertyName("modules_branch")]
    public string ModulesBranch { get; set; } = "main";

    /// <summary>Pull modules from DaisiGit on startup.</summary>
    [JsonPropertyName("pull_modules")]
    public bool PullModules { get; set; }

    // ── LoRA Evolution ──

    /// <summary>Path to a .llra adapter file to merge at model load. Null = no adapter.</summary>
    [JsonPropertyName("lora_adapter")]
    public string? LoraAdapter { get; set; }

    /// <summary>Directory for training data corpus and adapters.</summary>
    [JsonPropertyName("training_data_dir")]
    public string TrainingDataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "training");

    /// <summary>Minimum quality score (0.0-1.0) for a session to be exported as training data.</summary>
    [JsonPropertyName("training_min_quality")]
    public double TrainingMinQuality { get; set; } = 0.6;

    /// <summary>Auto-export sessions that meet quality threshold after goal completion.</summary>
    [JsonPropertyName("training_auto_export")]
    public bool TrainingAutoExport { get; set; }

    /// <summary>LoRA rank for training. Typical: 4, 8, 16.</summary>
    [JsonPropertyName("lora_rank")]
    public int LoraRank { get; set; } = 8;

    /// <summary>LoRA alpha scaling factor.</summary>
    [JsonPropertyName("lora_alpha")]
    public float LoraAlpha { get; set; } = 16.0f;

    /// <summary>Training epochs per run.</summary>
    [JsonPropertyName("training_epochs")]
    public int TrainingEpochs { get; set; } = 3;

    /// <summary>Training learning rate.</summary>
    [JsonPropertyName("training_lr")]
    public float TrainingLearningRate { get; set; } = 1e-4f;

    /// <summary>Training sequence length in tokens.</summary>
    [JsonPropertyName("training_seq_len")]
    public int TrainingSeqLen { get; set; } = 2048;

    [JsonPropertyName("models")]
    public List<ModelEntry> Models { get; set; } = [];
}

public sealed class ModelEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }
}
