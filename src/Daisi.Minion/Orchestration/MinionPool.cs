using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Daisi.Minion.Benchmarks;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Modules;
using Daisi.Minion.Types;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Orchestration;

/// <summary>
/// Manages a pool of child minions spawned by a SummonerMinion.
/// Children share the parent's model handle (serialized inference access)
/// but get their own conversation sessions and tool registries.
/// </summary>
public sealed class MinionPool : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ChildMinion> _children = new();
    private readonly DaisiLlogosModelHandle? _modelHandle;
    private readonly ConfigManager _configManager;
    private readonly ToolSandbox _sandbox;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private int _nextId;

    public MinionPool(DaisiLlogosModelHandle? modelHandle, ConfigManager configManager, ToolSandbox sandbox)
    {
        _modelHandle = modelHandle;
        _configManager = configManager;
        _sandbox = sandbox;
    }

    public IReadOnlyDictionary<string, ChildMinion> Children => _children;

    /// <summary>
    /// Spawn a new child minion with a specific type and task.
    /// </summary>
    public string Spawn(string typeName, string task, string? acceptanceCriteria = null, string? workingDirectory = null)
    {
        // Validate type first so callers get a clear error for bad type names
        var typeConfig = MinionTypeFactory.Get(typeName);

        if (_modelHandle == null)
            throw new InvalidOperationException("No model loaded. Cannot spawn child minions.");
        var id = $"{typeName}-{Interlocked.Increment(ref _nextId)}";

        // Create child's sandbox (subdirectory of parent, or custom)
        var childWorkDir = workingDirectory ?? _sandbox.Root;
        var childSandbox = new ToolSandbox(childWorkDir);

        // Register tools based on type config
        var toolRegistry = new CodingToolRegistry();
        var allowed = typeConfig.AllowedTools;
        IMinionTool[] allTools =
        [
            new FileReadTool(childSandbox),
            new FileWriteTool(childSandbox),
            new FileEditTool(childSandbox),
            new GrepTool(childSandbox),
            new GlobTool(childSandbox),
            new ShellExecuteTool(childSandbox),
            new GitTool(childSandbox),
        ];
        foreach (var tool in allTools)
        {
            if (allowed == null || allowed.Contains(tool.Name))
                toolRegistry.Register(tool);
        }

        // Build system prompt
        var systemPrompt = BuildChildSystemPrompt(id, typeConfig, task, childWorkDir, acceptanceCriteria);

        // Create conversation with shared model
        var toolDefs = toolRegistry.GetToolDefinitions();
        var conversation = new ConversationManager(systemPrompt, toolDefs);
        conversation.Initialize(_modelHandle);

        var child = new ChildMinion
        {
            Id = id,
            TypeName = typeName,
            Task = task,
            AcceptanceCriteria = acceptanceCriteria,
            Status = ChildMinionStatus.Idle,
            ToolRegistry = toolRegistry,
            Conversation = conversation,
            SpawnedAt = DateTime.UtcNow,
        };

        _children[id] = child;
        return id;
    }

    /// <summary>
    /// Send a task message to a child and run one agentic step.
    /// Returns the child's response.
    /// </summary>
    public async Task<string> SendAsync(string childId, string message, CancellationToken ct)
    {
        if (!_children.TryGetValue(childId, out var child))
            throw new ArgumentException($"Unknown child minion: {childId}");

        child.Status = ChildMinionStatus.Working;
        child.LastActivity = DateTime.UtcNow;
        child.Stopwatch.Start();

        var profile = GetGenerationParams();
        var toolFmt = MinionToolFormatter.Instance;

        // Serialize inference access — only one child uses the model at a time
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var response = await StreamToString(
                child.Conversation.SendAsync(message, profile, ct), ct);

            // Agentic tool loop
            while (toolFmt.ContainsToolCalls(response))
            {
                var toolCalls = toolFmt.ParseToolCalls(response);
                if (toolCalls.Count == 0) break;

                foreach (var call in toolCalls)
                {
                    var result = await child.ToolRegistry.ExecuteAsync(call, ct);
                    child.Conversation.AddToolResult(call.Name, result.Output);
                    child.ToolCallCount++;

                    // Track file modifications for evaluation
                    if (call.Name is "file_write" or "file_edit")
                    {
                        var path = call.Arguments["path"]?.ToString();
                        if (path != null) child.FilesModified.Add(path);
                    }
                }

                response = await StreamToString(
                    child.Conversation.ResumeAsync(profile, ct), ct);
            }

            child.LastResponse = response;
            child.IterationCount++;

            // Check for completion
            if (response.Contains("TASK_COMPLETE", StringComparison.OrdinalIgnoreCase))
                child.Status = ChildMinionStatus.Complete;
            else
                child.Status = ChildMinionStatus.Idle;

            return response;
        }
        catch (Exception ex)
        {
            child.Status = ChildMinionStatus.Failed;
            child.LastResponse = $"Error: {ex.Message}";
            throw;
        }
        finally
        {
            child.Stopwatch.Stop();
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Get the current status and last response of a child.
    /// </summary>
    public ChildMinionStatus GetStatus(string childId)
    {
        return _children.TryGetValue(childId, out var child)
            ? child.Status
            : throw new ArgumentException($"Unknown child minion: {childId}");
    }

    /// <summary>
    /// Stop and remove a child minion, freeing its resources.
    /// </summary>
    public void Stop(string childId)
    {
        if (_children.TryRemove(childId, out var child))
        {
            child.Status = ChildMinionStatus.Stopped;
            child.Conversation.Dispose();
        }
    }

    /// <summary>
    /// Get a summary of all children for the summoner's context.
    /// </summary>
    public string GetPoolSummary()
    {
        if (_children.IsEmpty) return "No active minions.";

        var sb = new StringBuilder();
        foreach (var (id, child) in _children)
        {
            sb.AppendLine($"- {id} [{child.Status}] task=\"{Truncate(child.Task, 60)}\" " +
                $"iterations={child.IterationCount} tools={child.ToolCallCount}");
        }
        return sb.ToString();
    }

    private static string BuildChildSystemPrompt(string id, MinionTypeConfig typeConfig, string task, string workDir, string? acceptanceCriteria = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are {id}, a {typeConfig.Name} minion worker.");
        sb.AppendLine($"Working directory: {workDir}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(typeConfig.SystemPromptExtension))
        {
            sb.AppendLine(typeConfig.SystemPromptExtension);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(acceptanceCriteria))
        {
            sb.AppendLine("# Acceptance Criteria");
            sb.AppendLine("Your work will be evaluated against these criteria. Meet ALL of them before declaring complete:");
            sb.AppendLine(acceptanceCriteria);
            sb.AppendLine();
        }
        sb.AppendLine("When your task is complete and all acceptance criteria are met, include TASK_COMPLETE in your response.");
        sb.AppendLine("If you're stuck, clearly explain what's blocking you.");
        return sb.ToString();
    }

    private GenerationParams GetGenerationParams()
    {
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

    private static async Task<string> StreamToString(IAsyncEnumerable<string> tokens, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var token in tokens.WithCancellation(ct))
            sb.Append(token);
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";

    public async ValueTask DisposeAsync()
    {
        foreach (var child in _children.Values)
            child.Conversation.Dispose();
        _children.Clear();
        _inferenceLock.Dispose();
    }
}

public sealed class ChildMinion
{
    public required string Id { get; init; }
    public required string TypeName { get; init; }
    public required string Task { get; init; }
    public string? AcceptanceCriteria { get; init; }
    public ChildMinionStatus Status { get; set; }
    public required CodingToolRegistry ToolRegistry { get; init; }
    public required ConversationManager Conversation { get; init; }
    public required DateTime SpawnedAt { get; init; }
    public DateTime? LastActivity { get; set; }
    public string? LastResponse { get; set; }
    public int IterationCount { get; set; }
    public int ToolCallCount { get; set; }

    // ── Evaluation metrics ──

    /// <summary>Names of modules that were active when this minion was spawned.</summary>
    public List<string> ActiveModules { get; init; } = [];

    /// <summary>Total tokens generated across all iterations.</summary>
    public int TotalTokens { get; set; }

    /// <summary>Stopwatch tracking total working time.</summary>
    public Stopwatch Stopwatch { get; } = new();

    /// <summary>Files this minion modified (for verification).</summary>
    public List<string> FilesModified { get; } = [];

    /// <summary>Summoner's evaluation score (set by evaluate_minion tool).</summary>
    public double? EvaluationScore { get; set; }

    /// <summary>Summoner's evaluation notes.</summary>
    public string? EvaluationNotes { get; set; }

    /// <summary>Build a TaskOutcome from this minion's metrics.</summary>
    public TaskOutcome BuildOutcome() => new()
    {
        Succeeded = Status == ChildMinionStatus.Complete,
        IterationsUsed = IterationCount,
        IterationBudget = 20,
        ToolCalls = ToolCallCount,
        TotalTokens = TotalTokens,
        Duration = Stopwatch.Elapsed,
        FilesModified = this.FilesModified.Count,
        WasStopped = Status == ChildMinionStatus.Stopped,
        TaskDescription = Task,
        MinionType = TypeName,
        SelfScore = EvaluationScore ?? 0,
        SelfNotes = EvaluationNotes,
    };
}

public enum ChildMinionStatus
{
    Idle,
    Working,
    Complete,
    Failed,
    Stopped,
}
