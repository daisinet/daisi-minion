namespace Daisi.Minion.Commands;

/// <summary>
/// Dispatches /slash commands to their handlers.
/// </summary>
public sealed class SlashCommandDispatcher
{
    private readonly Dictionary<string, ISlashCommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string command, ISlashCommandHandler handler)
    {
        _handlers[command] = handler;
    }

    /// <summary>
    /// Check if the input is a slash command.
    /// </summary>
    public bool IsCommand(string input) => input.StartsWith('/');

    /// <summary>
    /// Execute a slash command. Returns false if the command was not found.
    /// </summary>
    public async Task<bool> ExecuteAsync(string input, CancellationToken ct)
    {
        var parts = input.TrimStart('/').Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0];
        var args = parts.Length > 1 ? parts[1] : "";

        if (_handlers.TryGetValue(command, out var handler))
        {
            await handler.HandleAsync(args, ct);
            return true;
        }

        return false;
    }
}

/// <summary>
/// Handler for a slash command.
/// </summary>
public interface ISlashCommandHandler
{
    Task HandleAsync(string args, CancellationToken ct);
}
