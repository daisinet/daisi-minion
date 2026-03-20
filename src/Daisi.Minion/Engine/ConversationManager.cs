using Daisi.Llama.Chat;
using Daisi.Llama.Inference;

namespace Daisi.Minion.Engine;

/// <summary>
/// Manages the conversation state including the chat session, history, and context compaction.
/// </summary>
public sealed class ConversationManager : IDisposable
{
    private DaisiLlamaChatSession? _session;
    private DaisiLlamaModelHandle? _modelHandle;
    private readonly string _systemPrompt;
    private readonly List<ToolDefinition> _toolDefinitions;

    public bool HasSession => _session != null;
    public IReadOnlyList<ChatMessage>? History => _session?.History;

    public ConversationManager(string systemPrompt, List<ToolDefinition> toolDefinitions)
    {
        _systemPrompt = systemPrompt;
        _toolDefinitions = toolDefinitions;
    }

    /// <summary>
    /// Initialize with a loaded model handle.
    /// </summary>
    public void Initialize(DaisiLlamaModelHandle modelHandle)
    {
        _modelHandle = modelHandle;
        Reset();
    }

    /// <summary>
    /// Reset the conversation, starting a new chat session.
    /// </summary>
    public void Reset()
    {
        _session?.Dispose();
        if (_modelHandle == null) return;

        var fullPrompt = ToolPromptFormatter.BuildSystemPrompt(_systemPrompt, _toolDefinitions);
        _session = _modelHandle.CreateChatSession(fullPrompt);
    }

    /// <summary>
    /// Send a user message and stream back the response.
    /// </summary>
    public IAsyncEnumerable<string> SendAsync(string userMessage, GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null)
            throw new InvalidOperationException("No model loaded. Use /model to load a model.");

        return _session.ChatAsync(new ChatMessage("user", userMessage), parameters, ct);
    }

    /// <summary>
    /// Add a tool result to the conversation.
    /// </summary>
    public void AddToolResult(string result)
    {
        _session?.AddMessage(new ChatMessage("tool", result));
    }

    /// <summary>
    /// Resume generation after tool results have been injected.
    /// </summary>
    public IAsyncEnumerable<string> ResumeAsync(GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null)
            throw new InvalidOperationException("No model loaded.");

        // Send an empty assistant nudge to continue generation
        return _session.ChatAsync(new ChatMessage("user", ""), parameters, ct);
    }

    /// <summary>
    /// Compact the conversation by summarizing older messages.
    /// Keeps the system prompt and last N messages.
    /// </summary>
    public async Task CompactAsync(GenerationParams parameters, CancellationToken ct)
    {
        if (_session == null || _session.History.Count < 6) return;

        // Get a summary of the conversation so far
        var summaryPrompt = "Summarize the key context from our conversation so far in 2-3 sentences. Focus on what files were discussed, what changes were made, and what the current task is.";
        var summary = new System.Text.StringBuilder();

        await foreach (var token in _session.ChatAsync(new ChatMessage("user", summaryPrompt), parameters, ct))
            summary.Append(token);

        // Reset and inject the summary as context
        Reset();
        _session!.AddMessage(new ChatMessage("user", $"[Previous conversation summary: {summary}]"));
        _session.AddMessage(new ChatMessage("assistant", "Understood, I have the context from our previous conversation. How can I help?"));
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
