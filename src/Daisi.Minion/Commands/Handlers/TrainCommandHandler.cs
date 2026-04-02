using Daisi.Minion.Config;
using Daisi.Minion.Evolution;
using Daisi.Minion.Tui;
using Daisi.Llogos.Training;
using Daisi.Llogos.Training.Lora;

namespace Daisi.Minion.Commands.Handlers;

/// <summary>
/// /train — trigger LoRA training from accumulated corpus.
///
/// Usage:
///   /train              — train from all accumulated corpus data
///   /train --status     — show corpus statistics
///   /train --promote    — set the latest adapter as active in config
/// </summary>
public sealed class TrainCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager) : ISlashCommandHandler
{
    public async Task HandleAsync(string args, CancellationToken ct)
    {
        var sub = args.Trim().ToLowerInvariant();
        switch (sub)
        {
            case "--status" or "-s" or "status":
                ShowStatus();
                break;
            case "--promote" or "promote":
                PromoteAdapter();
                break;
            case "" or "--train" or "train":
                await RunTrainingAsync(ct);
                break;
            default:
                renderer.WriteInfo("Usage: /train, /train --status, /train --promote");
                break;
        }
    }

    private void ShowStatus()
    {
        var config = configManager.Config;
        var exporter = new TrainingDataExporter(config.TrainingDataDir);
        var files = exporter.GetCorpusFiles();

        renderer.WriteInfoHeader("Training Data Status");

        if (files.Length == 0)
        {
            renderer.WriteInfo("  No training data found.");
            renderer.WriteInfo($"  Enable auto-export: set training_auto_export=true in config.");
            renderer.WriteInfo($"  Data directory: {config.TrainingDataDir}");
            return;
        }

        int totalLines = 0;
        foreach (var file in files)
        {
            var lineCount = File.ReadLines(file).Count(l => !string.IsNullOrWhiteSpace(l));
            totalLines += lineCount;
            renderer.WriteInfo($"  {Path.GetFileName(file)}: {lineCount} sessions");
        }

        renderer.WriteInfo($"  Total: {totalLines} sessions across {files.Length} file(s)");
        renderer.WriteInfo($"  Quality threshold: {config.TrainingMinQuality:F2}");
        renderer.WriteInfo($"  LoRA: rank={config.LoraRank}, alpha={config.LoraAlpha}, epochs={config.TrainingEpochs}");

        if (!string.IsNullOrEmpty(config.LoraAdapter))
            renderer.WriteInfo($"  Active adapter: {Path.GetFileName(config.LoraAdapter)}");

        var adapterDir = Path.Combine(config.TrainingDataDir, "adapters");
        if (Directory.Exists(adapterDir))
        {
            var adapters = Directory.GetFiles(adapterDir, "*.llra");
            if (adapters.Length > 0)
            {
                renderer.WriteInfo($"  Trained adapters: {adapters.Length}");
                foreach (var a in adapters.OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
                    renderer.WriteInfo($"    {Path.GetFileName(a)} ({new FileInfo(a).Length / 1024} KB)");
            }
        }
    }

    private async Task RunTrainingAsync(CancellationToken ct)
    {
        var config = configManager.Config;
        var modelPath = config.ActiveModel;

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            renderer.WriteError("No model loaded. Cannot train without a base model.");
            return;
        }

        var exporter = new TrainingDataExporter(config.TrainingDataDir);
        var (mergedPath, lineCount) = exporter.MergeCorpus();

        if (lineCount == 0)
        {
            renderer.WriteError("No training data available. Run some goals with training_auto_export=true first.");
            return;
        }

        renderer.WriteInfoHeader($"LoRA Training: {lineCount} sessions");

        var adapterDir = Path.Combine(config.TrainingDataDir, "adapters");
        Directory.CreateDirectory(adapterDir);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var outputPath = Path.Combine(adapterDir, $"adapter-{timestamp}.llra");

        var trainingConfig = new TrainingConfig
        {
            ModelPath = modelPath,
            DataPath = mergedPath,
            Format = DataFormat.Jsonl,
            SeqLen = config.TrainingSeqLen,
            Lora = new LoraConfig
            {
                Rank = config.LoraRank,
                Alpha = config.LoraAlpha,
                Targets = LoraTarget.Q | LoraTarget.K | LoraTarget.V | LoraTarget.O,
            },
            Epochs = config.TrainingEpochs,
            LearningRate = config.TrainingLearningRate,
            OutputPath = outputPath,
            LogEverySteps = 5,
            SaveEverySteps = 0,
        };

        renderer.WriteInfo($"  Model: {Path.GetFileName(modelPath)}");
        renderer.WriteInfo($"  Data: {lineCount} sessions, seqLen={config.TrainingSeqLen}");
        renderer.WriteInfo($"  LoRA: rank={config.LoraRank}, alpha={config.LoraAlpha}");
        renderer.WriteInfo($"  Epochs: {config.TrainingEpochs}, LR: {config.TrainingLearningRate}");
        renderer.WriteInfo($"  Output: {Path.GetFileName(outputPath)}");
        renderer.WriteInfo("");

        try
        {
            await Task.Run(() =>
            {
                // Try CUDA backend first, fall back to CPU
                Daisi.Llogos.IComputeBackend? backend = null;
                try
                {
                    backend = new Daisi.Llogos.Cuda.CudaBackend();
                }
                catch
                {
                    // CUDA not available, fall through to CPU
                }

                using var session = new TrainingSession(trainingConfig, backend);
                session.Run();
            }, ct);

            renderer.WriteInfo("");
            renderer.WriteInfo($"Training complete: {Path.GetFileName(outputPath)}");
            renderer.WriteInfo("Activate with: /train --promote");
        }
        catch (OperationCanceledException)
        {
            renderer.WriteInfo("Training interrupted.");
        }
        catch (Exception ex)
        {
            renderer.WriteError($"Training failed: {ex.Message}");
        }
    }

    private void PromoteAdapter()
    {
        var config = configManager.Config;
        var adapterDir = Path.Combine(config.TrainingDataDir, "adapters");

        if (!Directory.Exists(adapterDir))
        {
            renderer.WriteError("No adapters found. Run /train first.");
            return;
        }

        var latest = Directory.GetFiles(adapterDir, "*.llra")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
        {
            renderer.WriteError("No adapter files found.");
            return;
        }

        config.LoraAdapter = latest;
        configManager.Save();
        renderer.WriteInfo($"Promoted: {Path.GetFileName(latest)}");
        renderer.WriteInfo("Adapter will load on next model reload (/model).");
    }
}
