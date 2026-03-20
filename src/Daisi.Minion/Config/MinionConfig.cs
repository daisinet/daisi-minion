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

    [JsonPropertyName("active_persona")]
    public string? ActivePersona { get; set; } = "coder";

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
