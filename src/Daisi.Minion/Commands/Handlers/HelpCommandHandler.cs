using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class HelpCommandHandler(AnsiRenderer renderer) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        renderer.WriteInfo("Available commands:");
        renderer.WriteInfo("  /help              Show this help");
        renderer.WriteInfo("  /clear             Clear conversation and start fresh");
        renderer.WriteInfo("  /compact           Summarize conversation to free context");
        renderer.WriteInfo("  /model             List and switch models");
        renderer.WriteInfo("  /model <url>       Download model from HuggingFace");
        renderer.WriteInfo("  /persona           List available personas");
        renderer.WriteInfo("  /persona <name>    Switch persona (resets conversation)");
        renderer.WriteInfo("  /exit              Exit daisi-minion");
        Console.WriteLine();
        return Task.CompletedTask;
    }
}
