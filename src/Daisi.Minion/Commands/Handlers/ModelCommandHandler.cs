using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

/// <summary>
/// Handles the /model command:
/// Also fetches and saves per-model inference profiles from HuggingFace.
/// - /model — list local models and active model
/// - /model &lt;huggingface-url&gt; — look up and download a model
/// </summary>
public sealed class ModelCommandHandler : ISlashCommandHandler
{
    private readonly AnsiRenderer _renderer;
    private readonly ConfigManager _configManager;

    public ModelCommandHandler(AnsiRenderer renderer, ConfigManager configManager)
    {
        _renderer = renderer;
        _configManager = configManager;
    }

    public async Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ListModels();
            return;
        }

        await DownloadModel(args, ct);
    }

    private void ListModels()
    {
        var config = _configManager.Config;
        _renderer.WriteInfo("Models directory: " + config.ModelsDirectory);
        _renderer.WriteInfo("");

        if (!Directory.Exists(config.ModelsDirectory))
        {
            _renderer.WriteError($"Models directory not found: {config.ModelsDirectory}");
            return;
        }

        var files = FindGgufFiles(config.ModelsDirectory);
        if (files.Count == 0)
        {
            _renderer.WriteInfo("No models found. Use /model <huggingface-url> to download one.");
            return;
        }

        var activePath = config.ActiveModel;
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var sizeMB = new FileInfo(file).Length / (1024.0 * 1024.0);
            var sizeGB = sizeMB / 1024.0;
            var sizeStr = sizeGB >= 1.0 ? $"{sizeGB:F1} GB" : $"{sizeMB:F0} MB";

            // Show path relative to models directory for subfolder models
            var relPath = Path.GetRelativePath(config.ModelsDirectory, file);
            var isActive = !string.IsNullOrEmpty(activePath)
                && string.Equals(Path.GetFullPath(file), Path.GetFullPath(activePath), StringComparison.OrdinalIgnoreCase);
            var active = isActive ? " \x1b[32m(active)\x1b[0m" : "";

            _renderer.WriteInfo($"  \x1b[0m\x1b[36m{i + 1}\x1b[0m) {relPath} \x1b[90m({sizeStr})\x1b[0m{active}");
        }

        _renderer.WriteInfo("");
        _renderer.WriteInfo("Use /model <number> to switch, or /model <huggingface-url> to download.");
    }

    /// <summary>
    /// Find all .gguf files in the models directory and its first-level subfolders.
    /// </summary>
    private static List<string> FindGgufFiles(string modelsDir)
    {
        var files = new List<string>();

        // Top-level files
        files.AddRange(Directory.EnumerateFiles(modelsDir, "*.gguf"));

        // First-level subfolder files
        foreach (var subDir in Directory.EnumerateDirectories(modelsDir))
            files.AddRange(Directory.EnumerateFiles(subDir, "*.gguf"));

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private async Task DownloadModel(string input, CancellationToken ct)
    {
        var repoId = HuggingFaceClient.ParseRepoId(input);
        if (repoId == null)
        {
            // Check if it's a number (switch model)
            if (int.TryParse(input, out var idx))
            {
                SwitchModel(idx);
                return;
            }

            _renderer.WriteError("Invalid input. Use a HuggingFace URL (e.g. Qwen/Qwen3.5-0.8B-GGUF) or a model number.");
            return;
        }

        _renderer.WriteInfo($"Looking up {repoId}...");

        var hf = new HuggingFaceClient(_renderer);
        var files = await hf.ListGgufFilesAsync(repoId, ct);

        if (files.Count == 0)
        {
            _renderer.WriteError("No GGUF files found in this repo.");
            return;
        }

        // Show options
        _renderer.WriteInfo("");
        _renderer.WriteInfo("Available quantizations:");
        for (int i = 0; i < files.Count; i++)
            _renderer.WriteInfo($"  [{i + 1}] {files[i].FileName} ({files[i].SizeDisplay})");

        _renderer.WriteInfo("");
        Console.Write("Select a file (number): ");
        var line = Console.ReadLine();
        if (!int.TryParse(line, out var selection) || selection < 1 || selection > files.Count)
        {
            _renderer.WriteError("Invalid selection.");
            return;
        }

        var selected = files[selection - 1];
        _renderer.WriteInfo($"Downloading {selected.FileName}...");

        var downloader = new ModelDownloader(_renderer, _configManager);
        var path = await downloader.DownloadAsync(selected.DownloadUrl, selected.FileName, ct);

        if (path != null)
        {
            // Fetch and save recommended generation settings
            // The GGUF repo is often a quantized fork — try the base model repo too
            _renderer.WriteInfo("Fetching model settings from HuggingFace...");
            var profile = await hf.FetchGenerationConfigAsync(repoId, ct);
            if (profile != null)
            {
                profile.ModelId = Path.GetFileNameWithoutExtension(selected.FileName);
                profile.Save(path);
                _renderer.WriteSuccess($"Saved model profile (temp={profile.Temperature}, top_k={profile.TopK}, top_p={profile.TopP}, ctx={profile.ContextSize})");
            }

            _renderer.WriteSuccess($"Model saved to {path}. Restart daisi-minion to use it.");
        }
    }

    private void SwitchModel(int index)
    {
        var dir = _configManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir)) return;

        var files = FindGgufFiles(dir);
        if (index < 1 || index > files.Count)
        {
            _renderer.WriteError("Invalid model number.");
            return;
        }

        _configManager.Config.ActiveModel = files[index - 1];
        _configManager.Save();
        _renderer.WriteSuccess($"Active model set to {Path.GetFileName(files[index - 1])}. Restart daisi-minion to use it.");
    }
}
