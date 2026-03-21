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
    private readonly RoleManager _roles = new();
    private readonly PersonaManager _personas = new();
    private ConversationManager? _conversation;
    private DaisiLlogosModelHandle? _modelHandle;
    private int _activeContextSize;
    private DualModeOrchestrator? _dualMode;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _inferenceCts;
    private string _lastResponse = "";

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

        _renderer.WriteBanner(_configManager.Config.MinionName);

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
                _renderer.WriteLine();
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
        _input.OnCycleRole(CycleRole);
        _input.OnCyclePersona(CyclePersona);
        _input.OnShowLastResponse(ShowRawLastResponse);
        _layout.Initialize();
    }

    private void SetInitialIndicators()
    {
        _layout?.StatusBar.SetStatus(_configManager.Config.MinionName);

        if (_modelHandle != null)
            _layout?.StatusBar.SetIndicator("model", _modelHandle.ModelId);

        if (!string.IsNullOrEmpty(_projectContext.GitBranch))
            _layout?.StatusBar.SetIndicator("branch", _projectContext.GitBranch);

        _layout?.StatusBar.SetContextUsage(_conversation?.ContextUsed ?? 0, _activeContextSize);
        _layout?.StatusBar.SetIndicator("backend", _configManager.Config.Backend);

        var role = _configManager.Config.ActiveRole;
        if (string.IsNullOrEmpty(role) || !_roles.Exists(role))
            role = "chat";
        _layout?.SetRoleLabel(role);

        _layout?.SetPersonaLabel(_configManager.Config.ActivePersona);

        _layout?.UpdateStatusBar();
    }

    private async Task ProcessUserMessage(string input)
    {
        var parameters = GetGenerationParams();
        var toolExecutor = new ToolExecutor(_toolRegistry, _renderer);

        // Per-inference cancellation, linked to the app-level CTS
        _inferenceCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _inferenceCts.Token;

        // Listen for double-Escape in the background to cancel inference
        _ = Task.Run(() => ListenForEscapeCancel(_inferenceCts));

        // Show spinner while generating
        _output?.UpdateStatus(sb => sb.StartSpinner("Thinking...", () =>
            _output?.TickSpinner()));

        try
        {
            var fullResponse = await StreamByLine(
                _conversation!.SendAsync(input, parameters, ct), ct);

            // Agentic tool loop
            var toolFmt = MinionToolFormatter.Instance;
            while (toolFmt.ContainsToolCalls(fullResponse))
            {
                var toolCalls = toolFmt.ParseToolCalls(fullResponse);
                if (toolCalls.Count == 0) break;

                _output?.UpdateStatus(sb =>
                {
                    sb.StopSpinner();
                    sb.StartSpinner($"Running {toolCalls.Count} tool(s)...", () =>
                        _output?.TickSpinner());
                });

                var results = await toolExecutor.ExecuteToolCallsAsync(toolCalls, ct);

                // Inject results and track file operations
                for (int i = 0; i < results.Count; i++)
                {
                    var (name, output) = results[i];
                    _conversation.AddToolResult(name, output);
                    TrackFileFromToolCall(toolCalls[i]);
                }

                _output?.UpdateStatus(sb =>
                {
                    sb.StopSpinner();
                    sb.StartSpinner("Generating...", () =>
                        _output?.TickSpinner());
                });

                fullResponse = await StreamByLine(
                    _conversation.ResumeAsync(parameters, ct), ct);
            }

            _lastResponse = fullResponse;

            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.RemoveIndicator("gen");
                sb.SetContextUsage(_conversation!.ContextUsed, _activeContextSize);
                sb.SetStatus(_configManager.Config.MinionName);
            });

            // Auto-compact if context usage >= 90%
            await TryAutoCompact();
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            _renderer.WriteInfo("(interrupted)");
            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.RemoveIndicator("gen");
                sb.SetContextUsage(_conversation?.ContextUsed ?? 0, _activeContextSize);
                sb.SetStatus("Interrupted");
            });
        }
        catch (OperationCanceledException)
        {
            // App shutdown
            throw;
        }
        catch (Exception ex)
        {
            _renderer.WriteError(ex.Message);
            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.RemoveIndicator("gen");
                sb.SetStatus($"Error: {ex.Message}");
            });
        }
        finally
        {
            _inferenceCts.Dispose();
            _inferenceCts = null;
        }
    }

    /// <summary>
    /// Poll for double-Escape keypress during inference. Two Escape keys within 500ms cancels.
    /// Consumes only Escape keys; other keys are ignored.
    /// </summary>
    private async Task RunGoalAsync(string goal, int maxIterations)
    {
        if (_conversation == null || !_conversation.HasSession)
        {
            _renderer.WriteError("No model loaded. Use /model to load a model.");
            return;
        }

        _renderer.WriteUserInput($"Goal: {goal}");

        // Send the initial goal message with autonomous instructions
        var goalPrompt = $"""
            Your goal: {goal}

            Work toward this goal autonomously. Use your tools to explore, read files, make changes, run commands — whatever is needed.

            After each step, evaluate your progress:
            - If the goal is NOT yet complete, explain what you'll do next and continue working.
            - If the goal IS complete, respond with exactly "GOAL_COMPLETE" on its own line, followed by a brief summary of what was accomplished.

            Do not ask the user for input. Make decisions yourself and keep going.
            """;

        _inferenceCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _inferenceCts.Token;
        _ = Task.Run(() => ListenForEscapeCancel(_inferenceCts));

        try
        {
            for (int iteration = 1; iteration <= maxIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();

                _output?.UpdateStatus(sb =>
                {
                    sb.StopSpinner();
                    sb.SetIndicator("goal", $"goal {iteration}/{maxIterations}");
                    sb.StartSpinner($"Goal iteration {iteration}...", () =>
                        _output?.TickSpinner());
                });

                var message = iteration == 1 ? goalPrompt : "Continue working toward the goal. If complete, respond with GOAL_COMPLETE.";

                var fullResponse = await StreamByLine(
                    _conversation.SendAsync(message, GetGenerationParams(), ct), ct);

                // Run tool calls
                var toolFmt = MinionToolFormatter.Instance;
                while (toolFmt.ContainsToolCalls(fullResponse))
                {
                    var toolCalls = toolFmt.ParseToolCalls(fullResponse);
                    if (toolCalls.Count == 0) break;

                    _output?.UpdateStatus(sb =>
                    {
                        sb.StopSpinner();
                        sb.StartSpinner($"Running {toolCalls.Count} tool(s)...", () =>
                            _output?.TickSpinner());
                    });

                    var toolExecutor = new ToolExecutor(_toolRegistry, _renderer);
                    var results = await toolExecutor.ExecuteToolCallsAsync(toolCalls, ct);

                    for (int j = 0; j < results.Count; j++)
                    {
                        var (name, output) = results[j];
                        _conversation.AddToolResult(name, output);
                        TrackFileFromToolCall(toolCalls[j]);
                    }

                    _output?.UpdateStatus(sb =>
                    {
                        sb.StopSpinner();
                        sb.StartSpinner($"Goal iteration {iteration}...", () =>
                            _output?.TickSpinner());
                    });

                    fullResponse = await StreamByLine(
                        _conversation.ResumeAsync(GetGenerationParams(), ct), ct);
                }

                // Check if the model declared the goal complete
                if (fullResponse.Contains("GOAL_COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    _renderer.WriteSuccess($"Goal completed in {iteration} iteration(s).");
                    break;
                }

                if (iteration == maxIterations)
                {
                    _renderer.WriteInfo($"Reached max iterations ({maxIterations}). Goal may not be fully complete.");
                }
            }
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            _renderer.WriteInfo("(goal interrupted)");
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Goal failed: {ex.Message}");
        }
        finally
        {
            _output?.UpdateStatus(sb =>
            {
                sb.StopSpinner();
                sb.RemoveIndicator("gen");
                sb.RemoveIndicator("goal");
                sb.SetStatus(_configManager.Config.MinionName);
            });
            _inferenceCts?.Dispose();
            _inferenceCts = null;
        }
    }

    private static void ListenForEscapeCancel(CancellationTokenSource inferenceCts)
    {
        var lastEscape = DateTime.MinValue;
        while (!inferenceCts.IsCancellationRequested)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(50);
                continue;
            }

            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                var now = DateTime.UtcNow;
                if ((now - lastEscape).TotalMilliseconds < 500)
                {
                    inferenceCts.Cancel();
                    return;
                }
                lastEscape = now;
            }
        }
    }

    /// <summary>
    /// Collect tokens, emitting each complete line to the output as it becomes available.
    /// Lines are flushed on newline characters or at end of stream.
    /// Returns the full accumulated response for tool call parsing.
    /// </summary>
    private async Task<string> StreamByLine(IAsyncEnumerable<string> tokens, CancellationToken ct = default)
    {
        var fullResponse = new StringBuilder();
        var lineBuffer = new StringBuilder();
        var tokenCount = 0;
        var lineWriter = _renderer.CreateLineWriter();
        var firstLine = true;

        // Qwen 3.5 may produce <think>...</think> at the start of the response.
        // Buffer everything until we know whether thinking is present, then strip it.
        var prefixBuffer = new StringBuilder();
        var thinkStripped = false; // true once we've resolved the think prefix

        await foreach (var token in tokens.WithCancellation(ct))
        {
            fullResponse.Append(token);
            tokenCount++;

            // Phase 1: Buffer prefix to detect and strip <think>...</think>
            if (!thinkStripped)
            {
                prefixBuffer.Append(token);
                var buf = prefixBuffer.ToString();

                // Check if we have a think block that's been closed
                if (buf.Contains("</think>"))
                {
                    var endIdx = buf.IndexOf("</think>", StringComparison.Ordinal);
                    var afterThink = buf[(endIdx + 8)..].TrimStart('\r', '\n');
                    thinkStripped = true;
                    prefixBuffer.Clear();
                    if (afterThink.Length > 0)
                        lineBuffer.Append(afterThink);
                    continue;
                }

                // If the buffer doesn't start with <think and we have enough to tell,
                // flush it as regular content
                var trimmed = buf.TrimStart();
                if (trimmed.Length > 7 && !trimmed.StartsWith("<think"))
                {
                    thinkStripped = true;
                    lineBuffer.Append(buf);
                    prefixBuffer.Clear();
                    // fall through to line emission below
                }
                else
                {
                    continue; // still accumulating prefix
                }
            }
            else
            {
                lineBuffer.Append(token);
            }

            if (tokenCount % 10 == 0)
            {
                var count = tokenCount;
                _output?.UpdateStatus(s =>
                {
                    s.SetIndicator("gen", $"{count} tok");
                    s.SetContextUsage(_conversation?.ContextUsed ?? 0, _activeContextSize);
                });
            }

            // Emit complete lines
            while (lineBuffer.ToString().Contains('\n'))
            {
                var text = lineBuffer.ToString();
                var nlIdx = text.IndexOf('\n');
                var line = text[..nlIdx];
                lineBuffer.Clear();
                lineBuffer.Append(text[(nlIdx + 1)..]);

                if (firstLine) { _renderer.WriteLine(); firstLine = false; }
                lineWriter.WriteLine(line);
            }
        }

        // Flush remaining partial line
        if (lineBuffer.Length > 0)
        {
            if (firstLine) { _renderer.WriteLine(); firstLine = false; }
            lineWriter.WriteLine(lineBuffer.ToString());
        }
        lineWriter.Finish();
        _renderer.WriteLine();

        _output?.UpdateStatus(s => s.SetIndicator("gen", $"{tokenCount} tok"));
        return fullResponse.ToString();
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

        _renderer.WriteInfoHeader($"Loading {Path.GetFileName(modelPath)}...");

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

            // Use per-model profile if available; fetch from HuggingFace if missing
            var profile = ModelProfile.Load(modelPath);
            if (profile == null)
                profile = await TryFetchProfileFromHuggingFace(modelPath);
            var contextSize = profile?.ContextSize ?? _configManager.Config.ContextSize;

            var handleAdapter = await backend.LoadModelAsync(new Daisi.Inference.Models.ModelLoadRequest
            {
                ModelId = Path.GetFileNameWithoutExtension(modelPath),
                FilePath = modelPath,
                ContextSize = (uint)contextSize,
            });

            _modelHandle = ((DaisiLlogosModelHandleAdapter)handleAdapter).Inner;
            _activeContextSize = contextSize;
            if (profile != null)
                _renderer.WriteInfo($"Profile: temp={profile.Temperature}, top_k={profile.TopK}, top_p={profile.TopP}, ctx={contextSize}");
            _renderer.WriteSuccess($"Loaded {_modelHandle.ModelId} ({_modelHandle.Config.Architecture}, {_modelHandle.Config.NumLayers}L, {_modelHandle.Config.HiddenDim}D)");
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to load model: {ex.Message}");
        }
    }

    /// <summary>
    /// Try to find the model on HuggingFace by searching for its filename,
    /// fetch generation config, and save a local profile.
    /// </summary>
    private async Task<ModelProfile?> TryFetchProfileFromHuggingFace(string modelPath)
    {
        var fileName = Path.GetFileName(modelPath);
        var modelName = Path.GetFileNameWithoutExtension(modelPath);

        _renderer.WriteInfoHeader($"No local profile found. Searching HuggingFace for {modelName}...");

        try
        {
            var hf = new HuggingFaceClient(_renderer);

            // Strategy 1: search by the GGUF filename
            var repoId = await SearchForRepo(hf, modelName, fileName);

            if (repoId == null)
            {
                // Strategy 2: strip quantization suffix and search for base model
                var baseName = StripQuantSuffix(modelName);
                if (baseName != modelName)
                    repoId = await SearchForBaseModel(hf, baseName);
            }

            if (repoId == null)
            {
                _renderer.WriteInfo("Could not find model on HuggingFace. Using default settings.");
                return null;
            }

            _renderer.WriteInfo($"Found repo: {repoId}");
            var profile = await hf.FetchGenerationConfigAsync(repoId, CancellationToken.None);
            if (profile != null)
            {
                profile.ModelId = modelName;
                profile.Save(modelPath);
                _renderer.WriteSuccess("Saved model profile from HuggingFace.");
            }
            return profile;
        }
        catch (Exception ex)
        {
            _renderer.WriteInfo($"HuggingFace lookup failed: {ex.Message}. Using default settings.");
            return null;
        }
    }

    /// <summary>Search HuggingFace for a GGUF repo matching a search term that contains the given file.</summary>
    private static async Task<string?> SearchForRepo(HuggingFaceClient hf, string? searchTerm, string ggufFileName)
    {
        if (string.IsNullOrEmpty(searchTerm)) return null;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("daisi-minion/1.0");

        // Search the HF API for GGUF repos matching the term
        var searchUrl = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(searchTerm)}&filter=gguf&limit=5";
        var json = await http.GetStringAsync(searchUrl);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        foreach (var model in doc.RootElement.EnumerateArray())
        {
            var id = model.GetProperty("id").GetString();
            if (id == null) continue;

            // Check if this repo has our exact file
            if (model.TryGetProperty("siblings", out var siblings))
            {
                foreach (var sib in siblings.EnumerateArray())
                {
                    var fname = sib.GetProperty("rfilename").GetString();
                    if (string.Equals(fname, ggufFileName, StringComparison.OrdinalIgnoreCase))
                        return id;
                }
            }
        }

        return null;
    }

    /// <summary>Search for the base (non-GGUF) model repo to get generation config.</summary>
    private static async Task<string?> SearchForBaseModel(HuggingFaceClient hf, string baseName)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("daisi-minion/1.0");

        var searchUrl = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(baseName)}&limit=5";
        var json = await http.GetStringAsync(searchUrl);
        var doc = System.Text.Json.JsonDocument.Parse(json);

        foreach (var model in doc.RootElement.EnumerateArray())
        {
            var id = model.GetProperty("id").GetString();
            if (id == null) continue;

            // Prefer repos that look like the original model (not GGUF forks)
            if (id.Contains("GGUF", StringComparison.OrdinalIgnoreCase)) continue;
            return id;
        }

        // If nothing else, take the first result
        foreach (var model in doc.RootElement.EnumerateArray())
            return model.GetProperty("id").GetString();

        return null;
    }

    /// <summary>Strip common GGUF quantization suffixes to get the base model name.</summary>
    private async Task TryAutoCompact()
    {
        if (_conversation == null) return;

        var used = _conversation.ContextUsed;
        var pct = _activeContextSize > 0 ? (double)used / _activeContextSize : 0;

        if (pct >= 0.90 && (_conversation.History?.Count ?? 0) >= 6)
        {
            _renderer.WriteInfo($"Context {pct:P0} full — auto-compacting...");
            try
            {
                await _conversation.CompactAsync(GetGenerationParams(), _cts.Token);
                var newUsed = _conversation.ContextUsed;
                _output?.UpdateStatus(sb =>
                    sb.SetContextUsage(newUsed, _activeContextSize));
                _renderer.WriteSuccess($"Compacted. Context: {newUsed / 1024.0:F1}k / {_activeContextSize / 1024}k");
            }
            catch (Exception ex)
            {
                _renderer.WriteError($"Auto-compact failed: {ex.Message}");
            }
        }
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

    private static string StripQuantSuffix(string name)
    {
        // Common patterns: Model-Q4_K_M, Model-q8_0, Model-IQ2_M, Model-f16
        var suffixes = new[] { "-Q", "-q", "-IQ", "-iq", "-f16", "-f32", "-F16", "-F32", ".Q", ".q", ".IQ", ".iq" };
        foreach (var suffix in suffixes)
        {
            var idx = name.IndexOf(suffix, StringComparison.Ordinal);
            if (idx > 0)
                return name[..idx];
        }
        // Also strip trailing -GGUF
        if (name.EndsWith("-GGUF", StringComparison.OrdinalIgnoreCase))
            return name[..^5];
        return name;
    }

    private string? PromptForModel()
    {
        var dir = _configManager.Config.ModelsDirectory;
        if (!Directory.Exists(dir))
            return null;

        var ggufFiles = FindGgufFiles(dir);

        if (ggufFiles.Count == 0)
            return null;

        if (ggufFiles.Count == 1)
        {
            var only = ggufFiles[0];
            _renderer.WriteInfo($"Found model: {Path.GetRelativePath(dir, only)}");
            return only;
        }

        _renderer.WriteInfoHeader("Available models:");
        Console.WriteLine();
        for (int i = 0; i < ggufFiles.Count; i++)
        {
            var relPath = Path.GetRelativePath(dir, ggufFiles[i]);
            var sizeMB = new FileInfo(ggufFiles[i]).Length / (1024.0 * 1024.0);
            var sizeGB = sizeMB / 1024.0;
            var sizeStr = sizeGB >= 1.0 ? $"{sizeGB:F1} GB" : $"{sizeMB:F0} MB";
            Console.WriteLine($"  \x1b[36m{i + 1}\x1b[0m) {relPath} \x1b[90m({sizeStr})\x1b[0m");
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
        var minionName = _configManager.Config.MinionName;
        sb.AppendLine($"You are {minionName}, a local AI assistant.");
        sb.AppendLine();

        // Inject role — fall back to "chat" if saved role doesn't exist
        var roleName = _configManager.Config.ActiveRole;
        if (string.IsNullOrEmpty(roleName) || !_roles.Exists(roleName))
            roleName = "chat";

        var roleContent = _roles.GetContent(roleName);
        if (roleContent != null)
        {
            sb.AppendLine(roleContent);
            sb.AppendLine();
        }

        // Inject personality trait (persona)
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

    private static List<string> FindGgufFiles(string modelsDir)
    {
        var files = new List<string>();
        files.AddRange(Directory.EnumerateFiles(modelsDir, "*.gguf"));
        foreach (var subDir in Directory.EnumerateDirectories(modelsDir))
            files.AddRange(Directory.EnumerateFiles(subDir, "*.gguf"));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private void RegisterCommands()
    {
        _commands.Register("help", new HelpCommandHandler(_renderer));
        _commands.Register("clear", new ClearCommandHandler(_conversation!, _renderer));
        _commands.Register("compact", new CompactCommandHandler(_conversation!, _renderer, GetGenerationParams));
        _commands.Register("model", new ModelCommandHandler(_renderer, _configManager, ReloadModel));
        _commands.Register("role", new RoleCommandHandler(_renderer, _roles, _configManager, () =>
        {
            _conversation?.Dispose();
            InitializeConversation();
            var name = _configManager.Config.ActiveRole;
            if (string.IsNullOrEmpty(name) || !_roles.Exists(name)) name = "chat";
            _layout?.SetRoleLabel(name);
            _output?.RedrawCommandBar("", 0);
        }));
        _commands.Register("persona", new PersonaCommandHandler(_renderer, _personas, _configManager, () =>
        {
            _conversation?.Dispose();
            InitializeConversation();
            _layout?.SetPersonaLabel(_configManager.Config.ActivePersona);
            _output?.RedrawCommandBar("", 0);
        }));
        _commands.Register("backend", new BackendCommandHandler(_renderer, _configManager, name =>
        {
            _layout?.StatusBar.SetIndicator("backend", name);
            _layout?.UpdateStatusBar();
            ReloadModel();
        }));
        _commands.Register("inf-settings", new InfSettingsCommandHandler(_renderer, _configManager,
            async (modelPath, ct) => await TryFetchProfileFromHuggingFace(modelPath),
            ReloadModel));
        _commands.Register("attention", new AttentionCommandHandler(_renderer, _configManager, ReloadModel));
        _commands.Register("name", new NameCommandHandler(_renderer, _configManager, name =>
        {
            _layout?.StatusBar.SetIndicator("name", name);
            _layout?.UpdateStatusBar();
            _conversation?.Dispose();
            InitializeConversation();
        }));
        _commands.Register("goal", new GoalCommandHandler(_renderer, RunGoalAsync));
    }

    private void ShowRawLastResponse()
    {
        if (string.IsNullOrEmpty(_lastResponse))
        {
            _renderer.WriteInfo("(no response yet)");
            return;
        }

        _renderer.WriteLine();
        _renderer.WriteInfoHeader("── Raw response ──");

        // Show the raw text with all tags, thinking, tool calls visible
        // Escape nothing — dump exactly what the model produced
        var escaped = _lastResponse
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        foreach (var line in escaped.Split('\n'))
            _renderer.WriteInfo(line);

        _renderer.WriteInfoHeader("── End raw ──");
        _renderer.WriteLine();
    }

    private void CycleRole()
    {
        var available = _roles.Available;
        if (available.Count == 0) return;

        var current = _configManager.Config.ActiveRole ?? "chat";
        var idx = -1;
        for (var i = 0; i < available.Count; i++)
            if (string.Equals(available[i], current, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        var next = available[(idx + 1) % available.Count];

        _configManager.Config.ActiveRole = next;
        _configManager.Save();

        _conversation?.Dispose();
        InitializeConversation();

        _layout?.SetRoleLabel(next);
        _output?.RedrawCommandBar("", 0);
        _renderer.WriteSuccess($"Role: {next}");
        _renderer.WriteLine();
    }

    private void CyclePersona()
    {
        var available = _personas.Available;
        // Cycle: none → first → second → ... → last → none
        var current = _configManager.Config.ActivePersona;

        string? next;
        if (string.IsNullOrEmpty(current))
        {
            next = available.Count > 0 ? available[0] : null;
        }
        else
        {
            var idx = -1;
            for (var i = 0; i < available.Count; i++)
                if (string.Equals(available[i], current, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            next = idx + 1 < available.Count ? available[idx + 1] : null;
        }

        _configManager.Config.ActivePersona = next;
        _configManager.Save();

        _conversation?.Dispose();
        InitializeConversation();

        _layout?.SetPersonaLabel(next);
        _output?.RedrawCommandBar("", 0);
        _renderer.WriteSuccess($"Persona: {next ?? "none"}");
        _renderer.WriteLine();
    }

    private void ReloadModel()
    {
        // Run synchronously on the calling thread to keep the CUDA context
        // on the same thread that will later run inference. Task.Run would
        // create the context on a threadpool thread, causing ErrorInvalidContext
        // when the main thread tries to use it.
        try
        {
            // Dispose in dependency order: conversation → dual-mode → model
            _conversation?.Dispose();
            _conversation = null;

            if (_dualMode != null)
            {
                _dualMode.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _dualMode = null;
            }

            _modelHandle?.Dispose();
            _modelHandle = null;

            // Allow GPU resources to fully release before creating new context
            GC.Collect();
            GC.WaitForPendingFinalizers();

            TryLoadModelAsync().GetAwaiter().GetResult();
            InitializeConversation();
            InitializeDualMode();
            SetInitialIndicators();
            _renderer.WriteSuccess("Model reloaded.");
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Reload failed: {ex.Message}");
        }
    }

    private GenerationParams GetGenerationParams()
    {
        // Use per-model profile if available, falling back to global config
        var modelPath = _configManager.Config.ActiveModel;
        var profile = !string.IsNullOrEmpty(modelPath) ? ModelProfile.Load(modelPath) : null;

        return new GenerationParams
        {
            MaxTokens = profile?.MaxTokens ?? _configManager.Config.MaxTokens,
            Temperature = profile?.Temperature ?? _configManager.Config.Temperature,
            TopK = profile?.TopK ?? 40,
            TopP = profile?.TopP ?? 0.9f,
            RepetitionPenalty = profile?.RepetitionPenalty ?? 1.1f,
        };
    }

    public void Dispose()
    {
        _layout?.Dispose();
        _dualMode?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _conversation?.Dispose();
        _modelHandle?.Dispose();
        _cts.Dispose();
    }
}
