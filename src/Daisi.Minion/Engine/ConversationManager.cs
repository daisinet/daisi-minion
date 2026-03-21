using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Engine;

/// <summary>
/// Manages the conversation state including the chat session, history, and context compaction.
/// Uses MinionChatRenderer for consistent prompt formatting across all models.
///
/// Tracks which files have been read and modified during the conversation.
/// On compaction, injects current file contents instead of stale historical reads.
/// During normal operation, the model is expected to re-read files before editing (option 4).
/// </summary>
public sealed class ConversationManager : IDisposable
{
    private DaisiLlogosChatSession? _session;
    private DaisiLlogosModelHandle? _modelHandle;
    private readonly string _systemPrompt;
    private readonly List<ToolDefinition> _toolDefinitions;
    private readonly MinionChatRenderer _renderer;
    private readonly MinionToolFormatter _toolFormatter = MinionToolFormatter.Instance;

    // Track files that have been read and/or modified during this conversation
    private readonly HashSet<string> _filesRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _filesModified = new(StringComparer.OrdinalIgnoreCase);

    public bool HasSession => _session != null;
    public IReadOnlyList<ChatMessage>? History => _session?.History;

    /// <summary>Number of tokens currently in the KV cache.</summary>
    public int ContextUsed => _session?.CachedTokenCount ?? 0;

    public ConversationManager(string systemPrompt, List<ToolDefinition> toolDefinitions)
    {
        _systemPrompt = systemPrompt;
        _toolDefinitions = toolDefinitions;
        _renderer = new MinionChatRenderer(toolDefinitions);
    }

    public void Initialize(DaisiLlogosModelHandle modelHandle)
    {
        _modelHandle = modelHandle;
        Reset();
    }

    public void Reset()
    {
        _session?.Dispose();
        _filesRead.Clear();
        _filesModified.Clear();
        if (_modelHandle == null) return;

        _session = _modelHandle.CreateChatSession(_systemPrompt, _renderer);
    }

    public IAsyncEnumerable<string> SendAsync(string userMessage, GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null)
            throw new InvalidOperationException("No model loaded. Use /model to load a model.");

        return TokenStreamFixer.Fix(_session.ChatAsync(new ChatMessage("user", userMessage), parameters, ct), ct);
    }

    /// <summary>
    /// Add a tool result and track file operations for smarter compaction.
    /// </summary>
    public void AddToolResult(string toolName, string result)
    {
        var msg = _toolFormatter.FormatToolResult(toolName, result);
        _session?.AddMessage(msg);

        // Track file operations for compaction context
        TrackFileOperation(toolName, result);
    }

    public IAsyncEnumerable<string> ResumeAsync(GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null)
            throw new InvalidOperationException("No model loaded.");

        return TokenStreamFixer.Fix(_session.ChatAsync(new ChatMessage("user", ""), parameters, ct), ct);
    }

    /// <summary>
    /// Compact the conversation. Resets the session and injects:
    /// 1. A summary of the conversation so far
    /// 2. Current contents of recently modified files (fresh from disk, not stale history)
    /// This gives the model accurate file state without KV cache cost during normal operation.
    /// </summary>
    public async Task CompactAsync(GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null || _session.History.Count < 6) return;

        // Get a summary of the conversation
        var summaryPrompt = "Summarize the key context from our conversation so far in 2-3 sentences. Focus on what files were discussed, what changes were made, and what the current task is.";
        var summary = new System.Text.StringBuilder();

        await foreach (var token in _session.ChatAsync(new ChatMessage("user", summaryPrompt), parameters, ct))
            summary.Append(token);

        // Collect current content of recently modified files
        var fileContext = BuildFileContext();

        // Reset and inject fresh context
        Reset();

        var contextMessage = $"[Previous conversation summary: {summary}]";
        if (fileContext.Length > 0)
            contextMessage += $"\n\n[Current state of modified files:\n{fileContext}]";

        _session!.AddMessage(new ChatMessage("user", contextMessage));
        _session.AddMessage(new ChatMessage("assistant", "Understood, I have the context and current file state from our previous conversation. How can I help?"));
    }

    /// <summary>
    /// Get the set of files that have been read during this conversation.
    /// </summary>
    public IReadOnlySet<string> FilesRead => _filesRead;

    /// <summary>
    /// Get the set of files that have been modified during this conversation.
    /// </summary>
    public IReadOnlySet<string> FilesModified => _filesModified;

    private void TrackFileOperation(string toolName, string result)
    {
        // Extract file path from the result — tool results typically start with the path
        // We parse based on tool name patterns
        switch (toolName)
        {
            case "file_read":
                // The path was in the tool call args, but we get the result here.
                // Extract path from the first line of output (format: "path:line content")
                var firstLine = result.Split('\n', 2)[0];
                // file_read results start with "  1\t" line-numbered content, path is in the tool call
                // We track via the tool call args in ExecuteToolCallsAsync instead
                break;

            case "file_write":
                // Result is like "Created foo.cs (42 lines)" or "Overwrote foo.cs (42 lines)"
                ExtractPathFromWriteResult(result);
                break;

            case "file_edit":
                // Result is like "Replaced 1 occurrence in foo.cs"
                ExtractPathFromEditResult(result);
                break;
        }
    }

    /// <summary>
    /// Track a file read by path (called by the engine after tool execution).
    /// </summary>
    public void TrackFileRead(string path)
    {
        _filesRead.Add(Path.GetFullPath(path));
    }

    /// <summary>
    /// Track a file modification by path (called by the engine after tool execution).
    /// </summary>
    public void TrackFileModified(string path)
    {
        _filesModified.Add(Path.GetFullPath(path));
    }

    private void ExtractPathFromWriteResult(string result)
    {
        // "Created path (N lines)" or "Overwrote path (N lines)"
        var prefixes = new[] { "Created ", "Overwrote " };
        foreach (var prefix in prefixes)
        {
            if (result.StartsWith(prefix))
            {
                var parenIdx = result.LastIndexOf(" (");
                if (parenIdx > prefix.Length)
                {
                    var path = result[prefix.Length..parenIdx];
                    _filesModified.Add(Path.GetFullPath(path));
                }
            }
        }
    }

    private void ExtractPathFromEditResult(string result)
    {
        // "Replaced N occurrence(s) in path"
        var inIdx = result.LastIndexOf(" in ");
        if (inIdx > 0)
        {
            var path = result[(inIdx + 4)..].TrimEnd('.');
            _filesModified.Add(Path.GetFullPath(path));
        }
    }

    /// <summary>
    /// Build a context block with current contents of recently modified files.
    /// Reads the actual files from disk so the model gets fresh state.
    /// </summary>
    private string BuildFileContext()
    {
        var sb = new System.Text.StringBuilder();
        var filesToInclude = _filesModified.Where(File.Exists).Take(5).ToList(); // Cap at 5 files

        foreach (var filePath in filesToInclude)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                // Truncate large files
                if (content.Length > 4000)
                    content = content[..4000] + "\n... (truncated)";

                var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);
                sb.AppendLine($"--- {relPath} ---");
                sb.AppendLine(content);
                sb.AppendLine();
            }
            catch
            {
                // File may have been deleted
            }
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
