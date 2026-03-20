using System.Text;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Commands;
using Daisi.Minion.Commands.Handlers;
using Daisi.Minion.Config;
using Daisi.Minion.Host;
using Daisi.Minion.Tui;
using Daisi.Minion.Tui.Layout;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;

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
    private readonly PersonaManager _personas = new();
    private ConversationManager? _conversation;
    private DaisiLlogosModelHandle? _modelHandle;
    private DualModeOrchestrator? _dualMode;
    private readonly CancellationTokenSource _cts = new();

    // Layout components
    private LayoutManager? _layout;
    private ConsoleOutput? _output;

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

        // Initialize layout (after model load so banner/load messages appear above)
        InitializeLayout();

        // Initialize conversation
        InitializeConversation();

        // Initialize dual-mode (host when idle)
        InitializeDualMode();

        // Register slash commands
        RegisterCommands();

        // Set status bar indicators
        SetInitialIndicators();

        // Main loop
        while (!_cts.IsCancellationRequested)
        {
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

                if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
                {
                    _layout?.ClearContent();
                    continue;
                }

                if (!await _commands.ExecuteAsync(input, _cts.Token))
                    _renderer.WriteError($"Unknown command: {input}. Type /help for available commands.");
                continue;
            }

            if (_conversation == null || !_conversation.HasSession)
            {
                _renderer.WriteError("No model loaded. Use /model to load a model.");
                continue;
            }

            _renderer.WriteUserInput(input);
            await ProcessUserMessage(input);
        }

        _renderer.WriteInfo("Goodbye!");
    }

    private void InitializeLayout()
    {
        _layout = new LayoutManager();
        _output = new ConsoleOutput(_layout);
        _renderer.SetOutput(_output);
        _input.SetLayout(_layout, _output);
        _layout.Initialize();
    }

    private void SetInitialIndicators()
    {
        if (_modelHandle != null)
            _layout?.StatusBar.SetIndicator("model", _modelHandle.ModelId);

        if (!string.IsNullOrEmpty(_projectContext.GitBranch))
            _layout?.StatusBar.SetIndicator("branch", _projectContext.GitBranch);

        _layout?.StatusBar.SetIndicator("ctx", $"{_configManager.Config.ContextSize / 1024}k ctx");

        var persona = _configManager.Config.ActivePersona;
        if (!string.IsNullOrEmpty(persona))
            _layout?.SetPersonaLabel(persona);

        _layout?.UpdateStatusBar();
    }

    private async Task ProcessUserMessage(string input)
    {
        var parameters = GetGenerationParams();
        var toolExecutor = new ToolExecutor(_toolRegistry, _renderer);

        // Show spinner while generating
        _output?.UpdateStatus(sb => sb.StartSpinner("Thinking...", () =>
            _output?.TickSpinner()));

        try
        {
            var fullResponse = await CollectResponse(
                _conversation!.SendAsync(input, parameters, _cts.Token));

            // Render the response
            _renderer.WriteResponse(fullResponse);

            // Agentic tool loop
            while (ToolCallParser.ContainsToolCalls(fullResponse))
            {
                var toolCalls = ToolCallParser.Parse(fullResponse);
                if (toolCalls.Count == 0) break;

                _output?.UpdateStatus(sb =>
                {
                    sb.StopSpinner();
                    sb.StartSpinner($"Running {toolCalls.Count} tool(s)...", () =>
                        _output?.TickSpinner());
                });

                var results = await toolExecutor.ExecuteToolCallsAsync(toolCalls, _cts.Token);

                foreach (var result in results)
                    _conversation.AddToolResult(result);

                _output?.UpdateStatus(sb =>
                {
                    sb.StopSpinner();
                    sb.StartSpinner("Generating...", () =>
                        _output?.TickSpinner());
                });

                fullResponse = await CollectResponse(
                    _conversation.ResumeAsync(parameters, _cts.Token));

                _renderer.WriteResponse(fullResponse);
            }

            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.RemoveIndicator("gen");
                sb.SetStatus("Ready");
            });
        }
        catch (OperationCanceledException)
        {
            _renderer.WriteInfo("(interrupted)");
            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.SetStatus("Interrupted");
            });
        }
        catch (Exception ex)
        {
            _renderer.WriteError(ex.Message);
            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.SetStatus($"Error: {ex.Message}");
            });
        }
    }

    private async Task<string> CollectResponse(IAsyncEnumerable<string> tokens)
    {
        var sb = new StringBuilder();
        var tokenCount = 0;
        await foreach (var token in tokens)
        {
            sb.Append(token);
            tokenCount++;
            if (tokenCount % 5 == 0) // Update every 5 tokens to avoid thrashing
                _output?.UpdateStatus(s => s.SetIndicator("gen", $"{tokenCount} tok"));
        }
        // Final count
        _output?.UpdateStatus(s => s.SetIndicator("gen", $"{tokenCount} tok"));
        return sb.ToString();
    }

    private async Task TryLoadModelAsync()
    {
        var modelPath = _configManager.Config.ActiveModel;

        // If saved model is missing or not set, prompt the user to pick one
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            modelPath = PromptForModel();
            if (modelPath != null)
            {
                _configManager.Config.ActiveModel = modelPath;
                _configManager.Save();
            }
        }

        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            _renderer.WriteError($"No model found. Place a GGUF model in {_configManager.Config.ModelsDirectory} or use /model to download one.");
            return;
        }

        _renderer.WriteInfo($"Loading {Path.GetFileName(modelPath)}...");

        // Apply thread count limit to CPU backend
        Daisi.Llogos.Cpu.CpuThreading.ThreadCount = _configManager.Config.ThreadCount;

        try
        {
            var backend = new DaisiLlogosTextBackend();
            backend.OnLog = msg => _renderer.WriteInfo(msg);
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

            _modelHandle = ((DaisiLlogosModelHandleAdapter)handleAdapter).Inner;
            _renderer.WriteSuccess($"Loaded {_modelHandle.ModelId} ({_modelHandle.Config.Architecture}, {_modelHandle.Config.NumLayers}L, {_modelHandle.Config.HiddenDim}D)");
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to load model: {ex.Message}");
        }
    }

    private string? PromptForModel()
    {
        var dir = _configManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir))
            return null;

        var ggufFiles = Directory.EnumerateFiles(dir, "*.gguf")
            .OrderBy(f => f)
            .ToList();

        if (ggufFiles.Count == 0)
            return null;

        if (ggufFiles.Count == 1)
        {
            var only = ggufFiles[0];
            _renderer.WriteInfo($"Found model: {Path.GetFileName(only)}");
            return only;
        }

        _renderer.WriteInfo("Available models:");
        Console.WriteLine();
        for (int i = 0; i < ggufFiles.Count; i++)
        {
            var name = Path.GetFileName(ggufFiles[i]);
            var sizeMB = new FileInfo(ggufFiles[i]).Length / (1024.0 * 1024.0);
            Console.WriteLine($"  \x1b[36m{i + 1}\x1b[0m) {name} \x1b[90m({sizeMB:F0} MB)\x1b[0m");
        }
        Console.WriteLine();
        Console.Write("Select a model [1]: ");

        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input))
            return ggufFiles[0];

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= ggufFiles.Count)
            return ggufFiles[choice - 1];

        _renderer.WriteError("Invalid selection, using first model.");
        return ggufFiles[0];
    }

    private void InitializeDualMode()
    {
        if (_modelHandle == null) return;

        var idleTimeout = TimeSpan.FromMinutes(_configManager.Config.IdleTimeoutMinutes);
        var activityMonitor = new ActivityMonitor(idleTimeout);
        var backend = new DaisiLlogosTextBackend();
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
        sb.AppendLine("You are daisi-minion, a local AI assistant.");
        sb.AppendLine();

        // Inject persona
        var personaName = _configManager.Config.ActivePersona;
        if (!string.IsNullOrEmpty(personaName))
        {
            var personaContent = _personas.GetContent(personaName);
            if (personaContent != null)
            {
                sb.AppendLine(personaContent);
                sb.AppendLine();
            }
        }

        sb.Append(_projectContext.ToSystemPromptSection());
        return sb.ToString();
    }

    private void RegisterCommands()
    {
        _commands.Register("help", new HelpCommandHandler(_renderer));
        _commands.Register("clear", new ClearCommandHandler(_conversation!, _renderer));
        _commands.Register("compact", new CompactCommandHandler(_conversation!, _renderer, GetGenerationParams));
        _commands.Register("model", new ModelCommandHandler(_renderer, _configManager));
        _commands.Register("persona", new PersonaCommandHandler(_renderer, _personas, _configManager, () =>
        {
            _conversation?.Dispose();
            InitializeConversation();
            var name = _configManager.Config.ActivePersona;
            _layout?.SetPersonaLabel(name ?? "");
            // Redraw command bar to show updated label
            _output?.RedrawCommandBar("", 0);
        }));
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
        _layout?.Dispose();
        _dualMode?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _conversation?.Dispose();
        _modelHandle?.Dispose();
        _cts.Dispose();
    }
}
