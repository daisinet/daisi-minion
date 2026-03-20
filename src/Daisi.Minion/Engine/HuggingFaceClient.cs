using System.Text.Json;
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
