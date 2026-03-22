using System.Text;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Config;
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
public sealed class CliRunner : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly CodingToolRegistry _toolRegistry = new();
    private readonly ProjectContext _projectContext;
    private readonly CancellationTokenSource _cts = new();
    private ConversationManager? _conversation;
    private DaisiLlogosModelHandle? _modelHandle;
    private int _activeContextSize;

    // CLI options
    private string? _goalArg;
    private string? _modelPathArg;
    private int? _contextSizeArg;
    private string? _backendArg;
    private int? _maxTokensArg;
    private int _maxIterations = 20;
    private string? _roleArg;
    private bool _jsonOutput;

    public CliRunner(ConfigManager configManager)
    {
        _configManager = configManager;
        _projectContext = new ProjectContext(Directory.GetCurrentDirectory());

        _toolRegistry.Register(new FileReadTool());
        _toolRegistry.Register(new FileWriteTool());
        _toolRegistry.Register(new FileEditTool());
        _toolRegistry.Register(new GrepTool());
        _toolRegistry.Register(new GlobTool());
        _toolRegistry.Register(new ShellExecuteTool());
        _toolRegistry.Register(new GitTool());
    }

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
                case "--json":
                    _jsonOutput = true;
                    break;
            }
        }
    }

    public async Task<int> RunAsync()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts.Cancel(); };

        await _projectContext.RefreshAsync(_cts.Token);

        if (!await LoadModelAsync())
            return 1;

        InitializeConversation();

        if (_goalArg != null)
            return await RunGoalAsync(_goalArg);

        return await RunInteractiveAsync();
    }

    private async Task<int> RunInteractiveAsync()
    {
        WriteInfo($"Model: {_modelHandle!.ModelId} ({_modelHandle.Config.Architecture})");
        WriteInfo($"Context: {_activeContextSize}, Backend: {_configManager.Config.Backend}");
        WriteInfo("Type your message (Ctrl+C to exit):");
        Console.Error.WriteLine();

        while (!_cts.IsCancellationRequested)
        {
            Console.Error.Write("> ");
            var input = Console.ReadLine();

            if (input == null) // EOF / Ctrl+C
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            await ProcessMessage(input);
            Console.Out.WriteLine(); // blank line between exchanges
        }

        return 0;
    }

    private async Task<int> RunGoalAsync(string goal)
    {
        WriteInfo($"Model: {_modelHandle!.ModelId}");
        WriteInfo($"Goal: {goal}");
        WriteInfo($"Max iterations: {_maxIterations}");

        var goalPrompt = $"""
            Your goal: {goal}

            Work toward this goal autonomously. Use your tools to explore, read files, make changes, run commands — whatever is needed.

            After each step, evaluate your progress:
            - If the goal is NOT yet complete, explain what you'll do next and continue working.
            - If the goal IS complete, respond with exactly "GOAL_COMPLETE" on its own line, followed by a brief summary of what was accomplished.

            Do not ask the user for input. Make decisions yourself and keep going.
            """;

        try
        {
            for (int iteration = 1; iteration <= _maxIterations; iteration++)
            {
                _cts.Token.ThrowIfCancellationRequested();

                WriteInfo($"--- Iteration {iteration}/{_maxIterations} ---");

                var message = iteration == 1
                    ? goalPrompt
                    : "Continue working toward the goal. If complete, respond with GOAL_COMPLETE.";

                var fullResponse = await RunAgenticStep(message);

                if (fullResponse.Contains("GOAL_COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    WriteInfo($"Goal completed in {iteration} iteration(s).");
                    return 0;
                }

                if (iteration == _maxIterations)
                {
                    WriteInfo($"Reached max iterations ({_maxIterations}). Goal may not be fully complete.");
                    return 2;
                }
            }
        }
        catch (OperationCanceledException)
        {
            WriteInfo("Goal interrupted.");
            return 130;
        }

        return 0;
    }

    private async Task ProcessMessage(string input)
    {
        try
        {
            await RunAgenticStep(input);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("(interrupted)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task<string> RunAgenticStep(string userMessage)
    {
        var parameters = GetGenerationParams();
        var ct = _cts.Token;

        var fullResponse = await StreamToConsole(
            _conversation!.SendAsync(userMessage, parameters, ct), ct);

        // Agentic tool loop
        var toolFmt = MinionToolFormatter.Instance;
        while (toolFmt.ContainsToolCalls(fullResponse))
        {
            var toolCalls = toolFmt.ParseToolCalls(fullResponse);
            if (toolCalls.Count == 0) break;

            for (int i = 0; i < toolCalls.Count; i++)
            {
                var call = toolCalls[i];
                WriteToolCall(call.Name, call.Arguments.ToJsonString());

                var result = await _toolRegistry.ExecuteAsync(call, ct);
                WriteToolResult(call.Name, result.Output, result.IsError);

                _conversation!.AddToolResult(call.Name, result.Output);
                TrackFileFromToolCall(call);
            }

            fullResponse = await StreamToConsole(
                _conversation!.ResumeAsync(parameters, ct), ct);
        }

        return fullResponse;
    }

    private async Task<string> StreamToConsole(IAsyncEnumerable<string> tokens, CancellationToken ct)
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

    private async Task<bool> LoadModelAsync()
    {
        var modelPath = _modelPathArg ?? _configManager.Config.ActiveModel;

        // If --model was explicitly passed, don't fall back to searching
        if (_modelPathArg != null && !File.Exists(_modelPathArg))
        {
            Console.Error.WriteLine($"Error: Model file not found: {_modelPathArg}");
            return false;
        }

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Try to find one in the models directory
            modelPath = FindFirstModel();
        }

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Error: No model found. Specify --model <path> or place a GGUF in {_configManager.Config.ModelsDirectory}");
            return false;
        }

        WriteInfo($"Loading {Path.GetFileName(modelPath)}...");

        var backend = _backendArg ?? _configManager.Config.Backend;
        Daisi.Llogos.Cpu.CpuThreading.ThreadCount = _configManager.Config.ThreadCount;

        try
        {
            var llogosBackend = new DaisiLlogosTextBackend();
            llogosBackend.OnLog = msg => WriteInfo(msg);
            await llogosBackend.ConfigureAsync(new Daisi.Inference.Models.BackendConfiguration
            {
                Runtime = backend,
            });

            var profile = ModelProfile.Load(modelPath);
            var contextSize = _contextSizeArg ?? profile?.ContextSize ?? _configManager.Config.ContextSize;

            var handleAdapter = await llogosBackend.LoadModelAsync(new Daisi.Inference.Models.ModelLoadRequest
            {
                ModelId = Path.GetFileNameWithoutExtension(modelPath),
                FilePath = modelPath,
                ContextSize = (uint)contextSize,
            });

            _modelHandle = ((DaisiLlogosModelHandleAdapter)handleAdapter).Inner;
            _activeContextSize = contextSize;
            WriteInfo($"Loaded {_modelHandle.ModelId} ({_modelHandle.Config.Architecture}, {_modelHandle.Config.NumLayers}L, ctx={contextSize})");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to load model: {ex.Message}");
            return false;
        }
    }

    private void InitializeConversation()
    {
        if (_modelHandle == null) return;

        var roleName = _roleArg ?? _configManager.Config.ActiveRole ?? "chat";
        var roles = new RoleManager();
        var sb = new StringBuilder();
        sb.AppendLine($"You name is {_configManager.Config.MinionName}, a local DAISI assistant.");
        sb.AppendLine();

        if (!roles.Exists(roleName)) roleName = "chat";
        var roleContent = roles.GetContent(roleName);
        if (roleContent != null)
        {
            sb.AppendLine(roleContent);
            sb.AppendLine();
        }

        sb.Append(_projectContext.ToSystemPromptSection());

        var toolDefs = _toolRegistry.GetToolDefinitions();
        _conversation = new ConversationManager(sb.ToString(), toolDefs);
        _conversation.Initialize(_modelHandle);
    }

    private string? FindFirstModel()
    {
        var dir = _configManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir)) return null;

        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(dir, "*.gguf"));
        foreach (var subDir in Directory.EnumerateDirectories(dir))
            files.AddRange(Directory.EnumerateFiles(subDir, "*.gguf"));

        return files.Count > 0 ? files[0] : null;
    }

    private GenerationParams GetGenerationParams()
    {
        var modelPath = _modelPathArg ?? _configManager.Config.ActiveModel;
        var profile = !string.IsNullOrEmpty(modelPath) ? ModelProfile.Load(modelPath) : null;

        return new GenerationParams
        {
            MaxTokens = _maxTokensArg ?? profile?.MaxTokens ?? _configManager.Config.MaxTokens,
            Temperature = profile?.Temperature ?? _configManager.Config.Temperature,
            TopK = profile?.TopK ?? 40,
            TopP = profile?.TopP ?? 0.9f,
            RepetitionPenalty = profile?.RepetitionPenalty ?? 1.1f,
        };
    }

    private void TrackFileFromToolCall(ToolCall call)
    {
        if (!call.Arguments.TryGetPropertyValue("path", out var pathNode)) return;
        var path = pathNode?.GetValue<string>();
        if (string.IsNullOrEmpty(path)) return;

        switch (call.Name)
        {
            case "file_read":
                _conversation?.TrackFileRead(path);
                break;
            case "file_write":
            case "file_edit":
                _conversation?.TrackFileModified(path);
                break;
        }
    }

    private void WriteInfo(string message) => Console.Error.WriteLine(message);

    private void WriteToolCall(string name, string argsJson)
    {
        if (_jsonOutput)
            Console.Error.WriteLine($"{{\"event\":\"tool_call\",\"name\":\"{Escape(name)}\",\"args\":{argsJson}}}");
        else
            Console.Error.WriteLine($"  [{name}] {TruncateArgs(argsJson)}");
    }

    private void WriteToolResult(string name, string output, bool isError)
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

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.Replace('\n', ' ') : s[..max].Replace('\n', ' ') + "...";

    private static string TruncateArgs(string json) =>
        json.Length <= 120 ? json : json[..120] + "...";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    public void Dispose()
    {
        _conversation?.Dispose();
        _modelHandle?.Dispose();
        _cts.Dispose();
    }
}
