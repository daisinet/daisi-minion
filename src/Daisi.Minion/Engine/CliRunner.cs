using System.Text;
using Daisi.Minion.Config;
using Daisi.Minion.Types;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Engine;

/// <summary>
/// Headless CLI runner for daisi-minion. Bypasses the TUI entirely and writes
/// plain text to stdout/stderr. Designed for scripting, testing, and CI pipelines.
///
/// Usage:
///   daisi-minion --cli                           Interactive stdin/stdout chat
///   daisi-minion --cli --goal "Fix the bug"      Run a goal autonomously, then exit
///   daisi-minion --cli --model path/to/model.gguf Override model path
///   daisi-minion --cli --context 4096            Override context size
///   daisi-minion --cli --backend cuda             Override backend (cpu/cuda/vulkan/auto)
///   daisi-minion --cli --max-tokens 2048         Override max generation tokens
///   daisi-minion --cli --max-iterations 10       Max iterations for goal mode (default 20)
///   daisi-minion --cli --role coder              Override active role
///   daisi-minion --cli --json                    Output structured JSON lines
/// </summary>
public sealed class CliRunner : MinionBase
{
    // CLI option overrides
    private string? _goalArg;
    private string? _modelPathArg;
    private int? _contextSizeArg;
    private string? _backendArg;
    private int? _maxTokensArg;
    private int _maxIterations = 20;
    private string? _roleArg;
    private string? _kvQuantArg;
    private int? _gpuLayersArg;
    private bool _jsonOutput;

    public CliRunner(ConfigManager configManager, MinionTypeConfig? typeConfig = null)
        : base(configManager, typeConfig) { }

    public void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--goal" when i + 1 < args.Length:
                    _goalArg = args[++i];
                    break;
                case "--model" when i + 1 < args.Length:
                    _modelPathArg = args[++i];
                    break;
                case "--context" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var ctx)) _contextSizeArg = ctx;
                    break;
                case "--backend" when i + 1 < args.Length:
                    _backendArg = args[++i];
                    break;
                case "--max-tokens" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var mt)) _maxTokensArg = mt;
                    break;
                case "--max-iterations" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var mi)) _maxIterations = mi;
                    break;
                case "--role" when i + 1 < args.Length:
                    _roleArg = args[++i];
                    break;
                case "--kv-quant" when i + 1 < args.Length:
                    _kvQuantArg = args[++i];
                    break;
                case "--gpu-layers" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out var gl)) _gpuLayersArg = gl;
                    break;
                case "--json":
                    _jsonOutput = true;
                    break;
            }
        }
    }

    public async Task<int> RunAsync()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cts.Cancel(); };

        await ProjectContext.RefreshAsync(Cts.Token);

        if (!await LoadModelAsync())
            return 1;

        InitializeConversation();

        if (_goalArg != null)
            return await RunGoalAsync(_goalArg);

        return await RunInteractiveAsync();
    }

    protected override string BuildSystemPrompt()
    {
        // Apply role override from CLI args
        if (_roleArg != null)
        {
            ConfigManager.Config.ActiveRole = _roleArg;
        }
        return base.BuildSystemPrompt();
    }

    // --- Presentation overrides ---

    protected override async Task<string> StreamTokensAsync(
        IAsyncEnumerable<string> tokens, CancellationToken ct)
    {
        var full = new StringBuilder();
        await foreach (var token in tokens.WithCancellation(ct))
        {
            full.Append(token);
            Console.Out.Write(token);
        }
        Console.Out.WriteLine();
        return full.ToString();
    }

    protected override void ReportToolCall(string name, string argsJson)
    {
        if (_jsonOutput)
            Console.Error.WriteLine($"{{\"event\":\"tool_call\",\"name\":\"{Escape(name)}\",\"args\":{argsJson}}}");
        else
            Console.Error.WriteLine($"  [{name}] {TruncateArgs(argsJson)}");
    }

    protected override void ReportToolResult(string name, string output, bool isError)
    {
        if (_jsonOutput)
        {
            var status = isError ? "error" : "ok";
            Console.Error.WriteLine($"{{\"event\":\"tool_result\",\"name\":\"{Escape(name)}\",\"status\":\"{status}\",\"output\":\"{Escape(Truncate(output, 500))}\"}}");
        }
        else
        {
            var prefix = isError ? "  ✗" : "  ✓";
            Console.Error.WriteLine($"{prefix} {name}: {Truncate(output, 200)}");
        }
    }

    protected override void ReportInfo(string message) => Console.Error.WriteLine(message);
    protected override void ReportError(string message) => Console.Error.WriteLine($"Error: {message}");

    // --- CLI-specific methods ---

    private async Task<int> RunInteractiveAsync()
    {
        ReportInfo($"Model: {ModelHandle!.ModelId} ({ModelHandle.Config.Architecture})");
        ReportInfo($"Context: {ActiveContextSize}, Backend: {ConfigManager.Config.Backend}");
        ReportInfo("Type your message (Ctrl+C to exit):");
        Console.Error.WriteLine();

        while (!Cts.IsCancellationRequested)
        {
            Console.Error.Write("> ");
            var input = Console.ReadLine();

            if (input == null) break;
            if (string.IsNullOrWhiteSpace(input)) continue;

            try
            {
                await RunAgenticStepAsync(input, Cts.Token);
                Console.Out.WriteLine();
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("(interrupted)");
            }
            catch (Exception ex)
            {
                ReportError(ex.Message);
            }
        }

        return 0;
    }

    private async Task<int> RunGoalAsync(string goal)
    {
        ReportInfo($"Model: {ModelHandle!.ModelId}");
        ReportInfo($"Goal: {goal}");
        ReportInfo($"Max iterations: {_maxIterations}");

        try
        {
            var (iterations, completed) = await RunGoalLoopAsync(goal, _maxIterations, Cts.Token);

            if (completed)
            {
                ReportInfo($"Goal completed in {iterations} iteration(s).");
                return 0;
            }
            else
            {
                ReportInfo($"Reached max iterations ({_maxIterations}). Goal may not be fully complete.");
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            ReportInfo("Goal interrupted.");
            return 130;
        }
    }

    private async Task<bool> LoadModelAsync()
    {
        var modelPath = _modelPathArg ?? ConfigManager.Config.ActiveModel;

        if (_modelPathArg != null && !File.Exists(_modelPathArg))
        {
            Console.Error.WriteLine($"Error: Model file not found: {_modelPathArg}");
            return false;
        }

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            modelPath = FindFirstModel();

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Error: No model found. Specify --model <path> or place a GGUF in {ConfigManager.Config.ModelsDirectory}");
            return false;
        }

        ReportInfo($"Loading {Path.GetFileName(modelPath)}...");

        var backend = _backendArg ?? ConfigManager.Config.Backend;
        Daisi.Llogos.Cpu.CpuThreading.ThreadCount = ConfigManager.Config.ThreadCount;

        try
        {
            var llogosBackend = new DaisiLlogosTextBackend();
            llogosBackend.OnLog = msg => ReportInfo(msg);
            await llogosBackend.ConfigureAsync(new Daisi.Inference.Models.BackendConfiguration
            {
                Runtime = backend,
            });

            var profile = ModelProfile.Load(modelPath);
            var contextSize = _contextSizeArg ?? profile?.ContextSize ?? ConfigManager.Config.ContextSize;

            var gpuLayers = _gpuLayersArg ?? 0;
            var handleAdapter = await llogosBackend.LoadModelAsync(new Daisi.Inference.Models.ModelLoadRequest
            {
                ModelId = Path.GetFileNameWithoutExtension(modelPath),
                FilePath = modelPath,
                ContextSize = (uint)contextSize,
                GpuLayerCount = gpuLayers,
            });

            ModelHandle = ((DaisiLlogosModelHandleAdapter)handleAdapter).Inner;
            ActiveContextSize = contextSize;
            ModelHandle.GpuLayerCount = gpuLayers;

            // Apply KV compression if configured
            var kvQuant = _kvQuantArg ?? ConfigManager.Config.KvQuant;
            if (!string.IsNullOrEmpty(kvQuant))
            {
                ModelHandle.TurboConfig = Daisi.Llogos.Inference.DaisiTurbo.TurboQuantConfig.Parse(kvQuant);
                var bpd = ModelHandle.TurboConfig.EffectiveBitsPerDim(ModelHandle.Config.KeyLength);
                ReportInfo($"KV compression: {kvQuant} ({bpd:F1} bits/dim)");
            }

            ReportInfo($"Loaded {ModelHandle.ModelId} ({ModelHandle.Config.Architecture}, {ModelHandle.Config.NumLayers}L, ctx={contextSize})");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to load model: {ex.Message}");
            return false;
        }
    }

    protected override GenerationParams GetGenerationParams()
    {
        var modelPath = ConfigManager.Config.ActiveModel;
        var profile = !string.IsNullOrEmpty(modelPath) ? ModelProfile.Load(modelPath) : null;

        return new GenerationParams
        {
            MaxTokens = _maxTokensArg ?? profile?.MaxTokens ?? ConfigManager.Config.MaxTokens,
            Temperature = profile?.Temperature ?? ConfigManager.Config.Temperature,
            TopK = profile?.TopK ?? 40,
            TopP = profile?.TopP ?? 0.9f,
            RepetitionPenalty = profile?.RepetitionPenalty ?? 1.1f,
        };
    }

    private string? FindFirstModel()
    {
        var dir = ConfigManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir)) return null;

        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(dir, "*.gguf"));
        foreach (var subDir in Directory.EnumerateDirectories(dir))
            files.AddRange(Directory.EnumerateFiles(subDir, "*.gguf"));

        return files.Count > 0 ? files[0] : null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.Replace('\n', ' ') : s[..max].Replace('\n', ' ') + "...";

    private static string TruncateArgs(string json) =>
        json.Length <= 120 ? json : json[..120] + "...";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
