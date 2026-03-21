using Daisi.Minion.Config;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class NameCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager,
    Action<string> onNameChanged) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            renderer.WriteInfoHeader($"Minion name: {configManager.Config.MinionName}");
            renderer.WriteInfo("Usage: /name <new name>");
            return Task.CompletedTask;
        }

        var name = args.Trim();
        configManager.Config.MinionName = name;
        configManager.Save();

        onNameChanged(name);
        renderer.WriteSuccess($"Minion renamed to: {name}");

        return Task.CompletedTask;
    }
}
