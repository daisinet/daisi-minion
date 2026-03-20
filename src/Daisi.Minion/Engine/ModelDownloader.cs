using Daisi.Minion.Config;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Engine;

/// <summary>
/// Downloads GGUF model files from HuggingFace with progress and resume support.
/// </summary>
public sealed class ModelDownloader
{
    private readonly HttpClient _http = new();
    private readonly AnsiRenderer _renderer;
    private readonly ConfigManager _configManager;

    public ModelDownloader(AnsiRenderer renderer, ConfigManager configManager)
    {
        _renderer = renderer;
        _configManager = configManager;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("daisi-minion/1.0");
    }

    /// <summary>
    /// Download a GGUF file from a direct URL.
    /// Supports HTTP Range for resume.
    /// </summary>
    public async Task<string?> DownloadAsync(string url, string fileName, CancellationToken ct)
    {
        var targetDir = _configManager.Config.ModelsDirectory;
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, fileName);
        var tempPath = targetPath + ".part";

        long existingSize = 0;
        if (File.Exists(tempPath))
            existingSize = new FileInfo(tempPath).Length;

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingSize > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingSize, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            _renderer.WriteError($"Download failed: HTTP {(int)response.StatusCode}");
            return null;
        }

        var totalSize = response.Content.Headers.ContentLength ?? 0;
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            totalSize += existingSize;
        else
            existingSize = 0; // Server doesn't support range, restart

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, existingSize > 0 ? FileMode.Append : FileMode.Create);

        var buffer = new byte[81920];
        long downloaded = existingSize;
        var lastUpdate = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;

            // Update progress every 500ms
            if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds > 500)
            {
                lastUpdate = DateTime.UtcNow;
                var pct = totalSize > 0 ? (double)downloaded / totalSize * 100 : 0;
                var downloadedMB = downloaded / (1024.0 * 1024.0);
                var totalMB = totalSize / (1024.0 * 1024.0);
                Console.Write($"\r  Downloading: {downloadedMB:F1} MB / {totalMB:F1} MB ({pct:F1}%)  ");
            }
        }

        Console.WriteLine($"\r  Download complete: {downloaded / (1024.0 * 1024.0):F1} MB                    ");

        // Rename temp to final
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tempPath, targetPath);

        // Update config
        _configManager.Config.Models.Add(new ModelEntry
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            Path = targetPath,
            SizeBytes = downloaded,
        });
        _configManager.Config.ActiveModel = targetPath;
        _configManager.Save();

        return targetPath;
    }
}
