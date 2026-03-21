using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class HelpCommandHandler(AnsiRenderer renderer) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        renderer.WriteInfoHeader("Available commands:");
        renderer.WriteInfo("  /help              Show this help");
        renderer.WriteInfo("  /clear             Clear conversation and start fresh");
        renderer.WriteInfo("  /compact           Summarize conversation to free context");
        renderer.WriteInfo("  /model             List and switch models");
        renderer.WriteInfo("  /model <url>       Download model from HuggingFace");
        renderer.WriteInfo("  /role              List available roles (coder, cto, chat...)");
        renderer.WriteInfo("  /role <name>       Switch role (resets conversation)");
        renderer.WriteInfo("  /persona           List available personas (witty, dry, sarcastic...)");
        renderer.WriteInfo("  /persona <name>    Set personality trait (resets conversation)");
        renderer.WriteInfo("  /backend           List available backends");
        renderer.WriteInfo("  /backend <name>    Switch backend (reloads model)");
        renderer.WriteInfo("  /inf-settings      Show inference settings for current model");
        renderer.WriteInfo("  /inf-settings k=v  Set inference params (temp, top_k, top_p, rep_pen, max, ctx)");
        renderer.WriteInfo("  /attention         Show attention strategy");
        renderer.WriteInfo("  /attention <mode>  Set strategy (full, window:N, sinks:S,W)");
        renderer.WriteInfo("  /goal <desc>       Work autonomously toward a goal");
        renderer.WriteInfo("  /goal <desc> --max=N  Limit to N iterations (default 20)");
        renderer.WriteInfo("  /name              Show minion name");
        renderer.WriteInfo("  /name <name>       Rename your minion");
        renderer.WriteInfo("  /exit              Exit daisi-minion");
        renderer.WriteInfo("");
        renderer.WriteInfo("  Shift+Tab          Cycle through roles");
        renderer.WriteInfo("  Ctrl+~             Cycle through personas");
        renderer.WriteInfo("  Esc Esc            Cancel active generation");
        Console.WriteLine();
        return Task.CompletedTask;
    }
}
