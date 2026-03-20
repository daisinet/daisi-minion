using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

/// <summary>
/// Handles the /model command:
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
        Console.WriteLine();

        // List GGUF files in the directory
        if (!Directory.Exists(config.ModelsDirectory))
        {
            _renderer.WriteError($"Models directory not found: {config.ModelsDirectory}");
            return;
        }

        var files = Directory.GetFiles(config.ModelsDirectory, "*.gguf");
        if (files.Length == 0)
        {
            _renderer.WriteInfo("No models found. Use /model <huggingface-url> to download one.");
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var size = new FileInfo(file).Length;
            var sizeMB = size / (1024.0 * 1024.0);
            var active = file == config.ActiveModel ? " \x1b[32m(active)\x1b[0m" : "";
            Console.WriteLine($"  {name} ({sizeMB:F0} MB){active}");
        }

        Console.WriteLine();
        _renderer.WriteInfo("Use /model <number> to switch, or /model <huggingface-url> to download.");
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
        Console.WriteLine();
        _renderer.WriteInfo("Available quantizations:");
        for (int i = 0; i < files.Count; i++)
            Console.WriteLine($"  [{i + 1}] {files[i].FileName} ({files[i].SizeDisplay})");

        Console.WriteLine();
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
            _renderer.WriteSuccess($"Model saved to {path}. Restart daisi-minion to use it.");
    }

    private void SwitchModel(int index)
    {
        var dir = _configManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.gguf");
        if (index < 1 || index > files.Length)
        {
            _renderer.WriteError("Invalid model number.");
            return;
        }

        _configManager.Config.ActiveModel = files[index - 1];
        _configManager.Save();
        _renderer.WriteSuccess($"Active model set to {Path.GetFileName(files[index - 1])}. Restart daisi-minion to use it.");
    }
}
