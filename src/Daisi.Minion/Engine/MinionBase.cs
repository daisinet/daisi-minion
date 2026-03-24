using System.Text;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Config;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Engine;

/// <summary>
/// Abstract base class for all minion types. Owns the core shared infrastructure:
/// model loading, conversation management, tool registry, and the agentic loop.
///
/// Subclasses provide presentation (TUI vs CLI) via abstract methods and can
/// override system prompt building, tool registration, and model loading.
/// </summary>
public abstract class MinionBase : IDisposable
{
    protected readonly ConfigManager ConfigManager;
    protected readonly CodingToolRegistry ToolRegistry = new();
    protected readonly ProjectContext ProjectContext;
    protected readonly RoleManager Roles = new();
    protected readonly PersonaManager Personas = new();
    protected readonly CancellationTokenSource Cts = new();

    protected ConversationManager? Conversation;
    protected DaisiLlogosModelHandle? ModelHandle;
    protected int ActiveContextSize;

    protected MinionBase(ConfigManager configManager)
    {
        ConfigManager = configManager;

        var workDir = configManager.Config.WorkingDirectory;
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
            workDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workDir);

        ProjectContext = new ProjectContext(workDir);
        RegisterBaseTools();
    }

    /// <summary>
    /// Register the 7 base tools and seal them so modules cannot replace them.
    /// Subclasses can call RegisterAdditionalTools() to add more before sealing.
    /// </summary>
    private void RegisterBaseTools()
    {
        ToolRegistry.Register(new FileReadTool());
        ToolRegistry.Register(new FileWriteTool());
        ToolRegistry.Register(new FileEditTool());
        ToolRegistry.Register(new GrepTool());
        ToolRegistry.Register(new GlobTool());
        ToolRegistry.Register(new ShellExecuteTool());
        ToolRegistry.Register(new GitTool());
        ToolRegistry.SealBaseTools();
    }

    /// <summary>
    /// Build the system prompt from role, persona, and project context.
    /// Subclasses can override to add type-specific instructions.
    /// </summary>
    protected virtual string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        var minionName = ConfigManager.Config.MinionName;
        sb.AppendLine($"You are {minionName}, a local AI assistant.");
        sb.AppendLine();

        var roleName = ConfigManager.Config.ActiveRole;
        if (string.IsNullOrEmpty(roleName) || !Roles.Exists(roleName))
            roleName = "chat";

        var roleContent = Roles.GetContent(roleName);
        if (roleContent != null)
        {
            sb.AppendLine(roleContent);
            sb.AppendLine();
        }

        var personaName = ConfigManager.Config.ActivePersona;
        if (!string.IsNullOrEmpty(personaName))
        {
            var personaContent = Personas.GetContent(personaName);
            if (personaContent != null)
            {
                sb.AppendLine(personaContent);
                sb.AppendLine();
            }
        }

        sb.Append(ProjectContext.ToSystemPromptSection());
        return sb.ToString();
    }

    /// <summary>
    /// Initialize the conversation with the current model and system prompt.
    /// </summary>
    protected void InitializeConversation()
    {
        if (ModelHandle == null) return;

        var systemPrompt = BuildSystemPrompt();
        var toolDefs = ToolRegistry.GetToolDefinitions();
        Conversation = new ConversationManager(systemPrompt, toolDefs);
        Conversation.Initialize(ModelHandle);
    }

    /// <summary>
    /// Get generation parameters from per-model profile or global config.
    /// Subclasses can override to apply CLI arg overrides.
    /// </summary>
    protected virtual GenerationParams GetGenerationParams()
    {
        var modelPath = ConfigManager.Config.ActiveModel;
        var profile = !string.IsNullOrEmpty(modelPath) ? ModelProfile.Load(modelPath) : null;

        return new GenerationParams
        {
            MaxTokens = profile?.MaxTokens ?? ConfigManager.Config.MaxTokens,
            Temperature = profile?.Temperature ?? ConfigManager.Config.Temperature,
            TopK = profile?.TopK ?? 40,
            TopP = profile?.TopP ?? 0.9f,
            RepetitionPenalty = profile?.RepetitionPenalty ?? 1.1f,
        };
    }

    /// <summary>
    /// The core agentic loop: send message, stream response, execute tool calls, repeat.
    /// Returns the final accumulated response text.
    /// </summary>
    protected async Task<string> RunAgenticStepAsync(string userMessage, CancellationToken ct)
    {
        var parameters = GetGenerationParams();

        var fullResponse = await StreamTokensAsync(
            Conversation!.SendAsync(userMessage, parameters, ct), ct);

        var toolFmt = MinionToolFormatter.Instance;
        while (toolFmt.ContainsToolCalls(fullResponse))
        {
            var toolCalls = toolFmt.ParseToolCalls(fullResponse);
            if (toolCalls.Count == 0) break;

            for (int i = 0; i < toolCalls.Count; i++)
            {
                var call = toolCalls[i];
                ReportToolCall(call.Name, call.Arguments.ToJsonString());

                var result = await ToolRegistry.ExecuteAsync(call, ct);
                ReportToolResult(call.Name, result.Output, result.IsError);

                Conversation.AddToolResult(call.Name, result.Output);
                TrackFileFromToolCall(call);
            }

            fullResponse = await StreamTokensAsync(
                Conversation.ResumeAsync(parameters, ct), ct);
        }

        return fullResponse;
    }

    /// <summary>
    /// Run a goal autonomously for up to maxIterations.
    /// Returns the number of iterations used and whether the goal completed.
    /// </summary>
    protected async Task<(int iterations, bool completed)> RunGoalLoopAsync(
        string goal, int maxIterations, CancellationToken ct)
    {
        var goalPrompt = $"""
            Your goal: {goal}

            Work toward this goal autonomously. Use your tools to explore, read files, make changes, run commands — whatever is needed.

            After each step, evaluate your progress:
            - If the goal is NOT yet complete, explain what you'll do next and continue working.
            - If the goal IS complete, respond with exactly "GOAL_COMPLETE" on its own line, followed by a brief summary of what was accomplished.

            Do not ask the user for input. Make decisions yourself and keep going.
            """;

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            ReportInfo($"--- Iteration {iteration}/{maxIterations} ---");

            var message = iteration == 1
                ? goalPrompt
                : "Continue working toward the goal. If complete, respond with GOAL_COMPLETE.";

            var fullResponse = await RunAgenticStepAsync(message, ct);

            if (fullResponse.Contains("GOAL_COMPLETE", StringComparison.OrdinalIgnoreCase))
                return (iteration, true);
        }

        return (maxIterations, false);
    }

    /// <summary>
    /// Track file operations from tool calls for context compaction.
    /// </summary>
    protected void TrackFileFromToolCall(ToolCall call)
    {
        if (!call.Arguments.TryGetPropertyValue("path", out var pathNode)) return;
        var path = pathNode?.GetValue<string>();
        if (string.IsNullOrEmpty(path)) return;

        switch (call.Name)
        {
            case "file_read":
                Conversation?.TrackFileRead(path);
                break;
            case "file_write":
            case "file_edit":
                Conversation?.TrackFileModified(path);
                break;
        }
    }

    /// <summary>
    /// Auto-compact if context usage >= 90%.
    /// </summary>
    protected async Task TryAutoCompactAsync()
    {
        if (Conversation == null) return;

        var used = Conversation.ContextUsed;
        var pct = ActiveContextSize > 0 ? (double)used / ActiveContextSize : 0;

        if (pct >= 0.90 && (Conversation.History?.Count ?? 0) >= 6)
        {
            ReportInfo($"Context {pct:P0} full — auto-compacting...");
            try
            {
                await Conversation.CompactAsync(GetGenerationParams(), Cts.Token);
                InferenceLog.Reset("auto-compact");
            }
            catch (Exception ex)
            {
                ReportError($"Auto-compact failed: {ex.Message}");
            }
        }
    }

    // --- Abstract presentation methods ---

    /// <summary>
    /// Stream tokens from the model to the user. Returns the full accumulated response.
    /// </summary>
    protected abstract Task<string> StreamTokensAsync(
        IAsyncEnumerable<string> tokens, CancellationToken ct);

    /// <summary>Report that a tool call is about to execute.</summary>
    protected abstract void ReportToolCall(string name, string argsJson);

    /// <summary>Report a tool call result.</summary>
    protected abstract void ReportToolResult(string name, string output, bool isError);

    /// <summary>Report an informational message.</summary>
    protected abstract void ReportInfo(string message);

    /// <summary>Report an error message.</summary>
    protected abstract void ReportError(string message);

    public virtual void Dispose()
    {
        Conversation?.Dispose();
        ModelHandle?.Dispose();
        Cts.Dispose();
    }
}
