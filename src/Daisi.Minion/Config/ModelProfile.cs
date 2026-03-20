using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daisi.Minion.Config;

/// <summary>
/// Per-model inference settings, fetched from HuggingFace and saved locally.
/// Stored as {model-id}.json in ~/.daisi-minion/models/.
/// </summary>
public sealed class ModelProfile
{
    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "";

    [JsonPropertyName("repo_id")]
    public string? RepoId { get; set; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.7f;

    [JsonPropertyName("top_k")]
    public int TopK { get; set; } = 40;

    [JsonPropertyName("top_p")]
    public float TopP { get; set; } = 0.9f;

    [JsonPropertyName("repetition_penalty")]
    public float RepetitionPenalty { get; set; } = 1.1f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("context_size")]
    public int ContextSize { get; set; } = 8192;

    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "models");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Load a profile for the given model file path, or null if none exists.</summary>
    public static ModelProfile? Load(string modelFilePath)
    {
        var profilePath = GetProfilePath(modelFilePath);
        if (!File.Exists(profilePath)) return null;

        var json = File.ReadAllText(profilePath);
        return JsonSerializer.Deserialize<ModelProfile>(json, JsonOpts);
    }

    /// <summary>Save this profile for the given model file path.</summary>
    public void Save(string modelFilePath)
    {
        Directory.CreateDirectory(ProfilesDir);
        var profilePath = GetProfilePath(modelFilePath);
        File.WriteAllText(profilePath, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>Check if a profile exists for the given model file path.</summary>
    public static bool Exists(string modelFilePath) => File.Exists(GetProfilePath(modelFilePath));

    private static string GetProfilePath(string modelFilePath)
    {
        var modelName = Path.GetFileNameWithoutExtension(modelFilePath);
        return Path.Combine(ProfilesDir, $"{modelName}.json");
    }
}
