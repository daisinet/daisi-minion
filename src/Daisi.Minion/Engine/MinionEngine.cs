using System.Text;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Commands;
using Daisi.Minion.Commands.Handlers;
using Daisi.Minion.Config;
using Daisi.Minion.Host;
using Daisi.Minion.Tui;
using Daisi.Llama.Chat;
using Daisi.Llama.Inference;

namespace Daisi.Minion.Engine;

/// <summary>
/// The main agentic loop for daisi-minion.
/// Handles: input → model inference → tool execution → display.
/// </summary>
public sealed class MinionEngine : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly AnsiRenderer _renderer = new();
    private readonly InputHandler _input = new();
    private readonly CodingToolRegistry _toolRegistry = new();
    private readonly SlashCommandDispatcher _commands = new();
    private readonly ProjectContext _projectContext;
    private ConversationManager? _conversation;
    private DaisiLlamaModelHandle? _modelHandle;
    private DualModeOrchestrator? _dualMode;
    private readonly CancellationTokenSource _cts = new();

    public MinionEngine(ConfigManager configManager)
    {
        _configManager = configManager;
        _projectContext = new ProjectContext(Directory.GetCurrentDirectory());

        // Register tools
        _toolRegistry.Register(new FileReadTool());
        _toolRegistry.Register(new FileWriteTool());
        _toolRegistry.Register(new FileEditTool());
        _toolRegistry.Register(new GrepTool());
        _toolRegistry.Register(new GlobTool());
        _toolRegistry.Register(new ShellExecuteTool());
        _toolRegistry.Register(new GitTool());
    }

    /// <summary>
    /// Run the main interactive loop.
    /// </summary>
    public async Task RunAsync()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts.Cancel(); };

        _renderer.WriteBanner();

        // Gather project context
        await _projectContext.RefreshAsync(_cts.Token);

        // Try to load the configured model
        await TryLoadModelAsync();

        // Initialize conversation
        InitializeConversation();

        // Initialize dual-mode (host when idle)
        InitializeDualMode();

        // Register slash commands
        RegisterCommands();

        // Main loop
        while (!_cts.IsCancellationRequested)
        {
            _renderer.WritePrompt();
            var input = _input.ReadLine();

            if (input == null) // Ctrl+C/D
                break;

            // Record activity for dual-mode tracking
            _dualMode?.RecordActivity();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (_commands.IsCommand(input))
            {
                if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!await _commands.ExecuteAsync(input, _cts.Token))
                    _renderer.WriteError($"Unknown command: {input}. Type /help for available commands.");
                continue;
            }

            if (_conversation == null || !_conversation.HasSession)
            {
                _renderer.WriteError("No model loaded. Use /model to load a model.");
                continue;
            }

            await ProcessUserMessage(input);
        }

        _renderer.WriteInfo("Goodbye!");
    }

    private async Task ProcessUserMessage(string input)
    {
        var parameters = GetGenerationParams();
        var toolExecutor = new ToolExecutor(_toolRegistry, _renderer);
        var display = new StreamingDisplay(_renderer);

        try
        {
            // Send message and stream response
            var responseBuilder = new StringBuilder();
            await foreach (var token in _conversation!.SendAsync(input, parameters, _cts.Token))
                display.WriteToken(token);

            var fullResponse = display.Finish();

            // Check for tool calls in the response
            while (ToolCallParser.ContainsToolCalls(fullResponse))
            {
                var toolCalls = ToolCallParser.Parse(fullResponse);
                if (toolCalls.Count == 0) break;

                // Execute tools
                var results = await toolExecutor.ExecuteToolCallsAsync(toolCalls, _cts.Token);

                // Inject results into conversation
                foreach (var result in results)
                    _conversation.AddToolResult(result);

                // Resume generation
                display = new StreamingDisplay(_renderer);
                await foreach (var token in _conversation.ResumeAsync(parameters, _cts.Token))
                    display.WriteToken(token);

                fullResponse = display.Finish();
            }
        }
        catch (OperationCanceledException)
        {
            display.Finish();
            _renderer.WriteInfo("\n(interrupted)");
        }
        catch (Exception ex)
        {
            display.Finish();
            _renderer.WriteError(ex.Message);
        }
    }

    private async Task TryLoadModelAsync()
    {
        var modelPath = _configManager.Config.ActiveModel;
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            // Try to find any GGUF in the models directory
            var dir = _configManager.Config.ModelsDirectory;
            if (Directory.Exists(dir))
            {
                modelPath = Directory.EnumerateFiles(dir, "*.gguf").FirstOrDefault();
                if (modelPath != null)
                {
                    _configManager.Config.ActiveModel = modelPath;
                    _configManager.Save();
                }
            }
        }

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            _renderer.WriteError($"No model found. Place a GGUF model in {_configManager.Config.ModelsDirectory} or use /model to download one.");
            return;
        }

        _renderer.WriteInfo($"Loading {Path.GetFileName(modelPath)}...");

        try
        {
            var backend = new DaisiLlamaTextBackend();
            await backend.ConfigureAsync(new Daisi.Inference.Models.BackendConfiguration
            {
                Runtime = _configManager.Config.Backend,
            });

            var handleAdapter = await backend.LoadModelAsync(new Daisi.Inference.Models.ModelLoadRequest
            {
                ModelId = Path.GetFileNameWithoutExtension(modelPath),
                FilePath = modelPath,
                ContextSize = (uint)_configManager.Config.ContextSize,
            });

            _modelHandle = ((DaisiLlamaModelHandleAdapter)handleAdapter).Inner;
            _renderer.WriteSuccess($"Loaded {_modelHandle.ModelId} ({_modelHandle.Config.Architecture}, {_modelHandle.Config.NumLayers}L, {_modelHandle.Config.HiddenDim}D)");
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to load model: {ex.Message}");
        }
    }

    private void InitializeDualMode()
    {
        if (_modelHandle == null) return;

        var idleTimeout = TimeSpan.FromMinutes(_configManager.Config.IdleTimeoutMinutes);
        var activityMonitor = new ActivityMonitor(idleTimeout);
        var backend = new DaisiLlamaTextBackend();
        var hostService = new HostModeService(_renderer, backend);
        _dualMode = new DualModeOrchestrator(activityMonitor, hostService, _renderer);
        _dualMode.Start(_modelHandle);
    }

    private void InitializeConversation()
    {
        if (_modelHandle == null) return;

        var systemPrompt = BuildSystemPrompt();
        var toolDefs = _toolRegistry.GetToolDefinitions();
        _conversation = new ConversationManager(systemPrompt, toolDefs);
        _conversation.Initialize(_modelHandle);
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are daisi-minion, a local AI coding assistant. You help users with software engineering tasks by reading, writing, and editing files, running commands, and navigating codebases.");
        sb.AppendLine();
        sb.AppendLine("Guidelines:");
        sb.AppendLine("- Be concise and direct. Lead with the answer or action.");
        sb.AppendLine("- Use tools to explore and modify the codebase. Don't guess — read files first.");
        sb.AppendLine("- When editing files, use file_edit for surgical changes, file_write for new files.");
        sb.AppendLine("- Show your reasoning briefly, then act.");
        sb.AppendLine("- If you need to run a command, use the shell tool.");
        sb.AppendLine();
        sb.Append(_projectContext.ToSystemPromptSection());
        return sb.ToString();
    }

    private void RegisterCommands()
    {
        _commands.Register("help", new HelpCommandHandler(_renderer));
        _commands.Register("clear", new ClearCommandHandler(_conversation!, _renderer));
        _commands.Register("compact", new CompactCommandHandler(_conversation!, _renderer, GetGenerationParams));
        _commands.Register("model", new ModelCommandHandler(_renderer, _configManager));
    }

    private GenerationParams GetGenerationParams() => new()
    {
        MaxTokens = _configManager.Config.MaxTokens,
        Temperature = _configManager.Config.Temperature,
        TopK = 40,
        TopP = 0.9f,
        RepetitionPenalty = 1.1f,
    };

    public void Dispose()
    {
        _dualMode?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _conversation?.Dispose();
        _modelHandle?.Dispose();
        _cts.Dispose();
    }
}
