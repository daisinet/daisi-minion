using System.Text.Json;
using Daisi.Minion.Config;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Engine;

/// <summary>
/// Queries HuggingFace API for GGUF model information.
/// </summary>
public sealed class HuggingFaceClient
{
    private readonly HttpClient _http = new();
    private readonly AnsiRenderer _renderer;

    public HuggingFaceClient(AnsiRenderer renderer)
    {
        _renderer = renderer;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("daisi-minion/1.0");
    }

    /// <summary>
    /// Look up GGUF files available for a HuggingFace model repo.
    /// </summary>
    /// <param name="repoId">e.g. "Qwen/Qwen3.5-0.8B-GGUF"</param>
    public async Task<List<GgufFileInfo>> ListGgufFilesAsync(string repoId, CancellationToken ct)
    {
        var url = $"https://huggingface.co/api/models/{repoId}?blobs=true&expand[]=gguf";

        try
        {
            var response = await _http.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(response);

            var results = new List<GgufFileInfo>();

            if (doc.RootElement.TryGetProperty("siblings", out var siblings))
            {
                foreach (var sibling in siblings.EnumerateArray())
                {
                    var name = sibling.GetProperty("rfilename").GetString() ?? "";
                    if (!name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    long size = 0;
                    if (sibling.TryGetProperty("size", out var sizeElem))
                        size = sizeElem.GetInt64();

                    results.Add(new GgufFileInfo
                    {
                        FileName = name,
                        SizeBytes = size,
                        DownloadUrl = $"https://huggingface.co/{repoId}/resolve/main/{name}",
                    });
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to query HuggingFace: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Fetch generation_config.json from a HuggingFace repo and extract recommended params.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    public async Task<ModelProfile?> FetchGenerationConfigAsync(string repoId, CancellationToken ct)
    {
        // Try generation_config.json first, then config.json
        var profile = await TryFetchGenerationConfig(repoId, "generation_config.json", ct);
        if (profile != null)
        {
            // Supplement with config.json for context_size if available
            await TryEnrichFromConfig(repoId, profile, ct);
            return profile;
        }

        // Fall back to config.json only
        profile = new ModelProfile { RepoId = repoId };
        await TryEnrichFromConfig(repoId, profile, ct);
        return profile;
    }

    private async Task<ModelProfile?> TryFetchGenerationConfig(string repoId, string fileName, CancellationToken ct)
    {
        try
        {
            var url = $"https://huggingface.co/{repoId}/resolve/main/{fileName}";
            var json = await _http.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var profile = new ModelProfile { RepoId = repoId };

            if (root.TryGetProperty("temperature", out var temp) && temp.ValueKind == JsonValueKind.Number)
                profile.Temperature = temp.GetSingle();
            if (root.TryGetProperty("top_k", out var topK) && topK.ValueKind == JsonValueKind.Number)
                profile.TopK = topK.GetInt32();
            if (root.TryGetProperty("top_p", out var topP) && topP.ValueKind == JsonValueKind.Number)
                profile.TopP = topP.GetSingle();
            if (root.TryGetProperty("repetition_penalty", out var repPen) && repPen.ValueKind == JsonValueKind.Number)
                profile.RepetitionPenalty = repPen.GetSingle();
            if (root.TryGetProperty("max_new_tokens", out var maxNew) && maxNew.ValueKind == JsonValueKind.Number)
                profile.MaxTokens = maxNew.GetInt32();
            else if (root.TryGetProperty("max_length", out var maxLen) && maxLen.ValueKind == JsonValueKind.Number)
                profile.MaxTokens = maxLen.GetInt32();

            return profile;
        }
        catch
        {
            return null;
        }
    }

    private async Task TryEnrichFromConfig(string repoId, ModelProfile profile, CancellationToken ct)
    {
        try
        {
            var url = $"https://huggingface.co/{repoId}/resolve/main/config.json";
            var json = await _http.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Context size from various field names
            if (root.TryGetProperty("max_position_embeddings", out var maxPos) && maxPos.ValueKind == JsonValueKind.Number)
                profile.ContextSize = maxPos.GetInt32();
            else if (root.TryGetProperty("max_seq_len", out var maxSeq) && maxSeq.ValueKind == JsonValueKind.Number)
                profile.ContextSize = maxSeq.GetInt32();
            else if (root.TryGetProperty("sliding_window", out var sw) && sw.ValueKind == JsonValueKind.Number)
                profile.ContextSize = sw.GetInt32();

            // Some models put generation params in config.json too
            if (profile.Temperature == 0.7f && root.TryGetProperty("temperature", out var temp) && temp.ValueKind == JsonValueKind.Number)
                profile.Temperature = temp.GetSingle();
            if (profile.TopK == 40 && root.TryGetProperty("top_k", out var topK) && topK.ValueKind == JsonValueKind.Number)
                profile.TopK = topK.GetInt32();
            if (profile.TopP == 0.9f && root.TryGetProperty("top_p", out var topP) && topP.ValueKind == JsonValueKind.Number)
                profile.TopP = topP.GetSingle();
        }
        catch
        {
            // config.json not available or not parsable — that's fine
        }
    }

    /// <summary>
    /// Parse a HuggingFace URL to extract the repo ID.
    /// Supports: https://huggingface.co/org/model, org/model
    /// </summary>
    public static string? ParseRepoId(string input)
    {
        input = input.Trim();

        // Full URL
        if (input.StartsWith("https://huggingface.co/", StringComparison.OrdinalIgnoreCase))
        {
            var path = input["https://huggingface.co/".Length..].TrimEnd('/');
            var parts = path.Split('/');
            if (parts.Length >= 2)
                return $"{parts[0]}/{parts[1]}";
        }

        // Bare repo ID (org/model) — must not contain "://" and must have exactly format "word/word"
        if (!input.Contains("://") && input.Contains('/') && !input.Contains(' '))
        {
            var parts = input.Split('/');
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
                return $"{parts[0]}/{parts[1]}";
        }

        return null;
    }
}

public sealed class GgufFileInfo
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DownloadUrl { get; set; } = "";

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
    };
}
