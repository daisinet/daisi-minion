using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class PersonaCommandHandler(
    AnsiRenderer renderer,
    PersonaManager personas,
    ConfigManager configManager,
    Action onPersonaChanged) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            // List available personas
            var current = configManager.Config.ActivePersona;
            renderer.WriteInfo("Available personas:");
            foreach (var name in personas.Available)
            {
                var marker = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                    ? " \x1b[32m(active)\x1b[0m" : "";
                renderer.WriteInfo($"  {name}{marker}");
            }
            renderer.WriteInfo("");
            renderer.WriteInfo("Usage: /persona <name>");
            return Task.CompletedTask;
        }

        var personaName = args.Trim();
        if (!personas.Exists(personaName))
        {
            renderer.WriteError($"Unknown persona: {personaName}");
            renderer.WriteInfo($"Available: {string.Join(", ", personas.Available)}");
            return Task.CompletedTask;
        }

        configManager.Config.ActivePersona = personaName;
        configManager.Save();

        renderer.WriteSuccess($"Persona set to: {personaName}");
        renderer.WriteInfo("Conversation reset with new persona.");

        onPersonaChanged();

        return Task.CompletedTask;
    }
}
