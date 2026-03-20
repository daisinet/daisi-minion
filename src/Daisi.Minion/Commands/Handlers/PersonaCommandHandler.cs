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
            ListPersonas();
            return Task.CompletedTask;
        }

        if (args.Trim().Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            personas.Reload();
            renderer.WriteSuccess($"Reloaded {personas.Available.Count} personas from disk.");
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

    private void ListPersonas()
    {
        var current = configManager.Config.ActivePersona;
        renderer.WriteInfo("Available personas:");
        foreach (var name in personas.Available)
        {
            var marker = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                ? " \x1b[32m(active)\x1b[0m" : "";
            renderer.WriteInfo($"  {name}{marker}");
        }
        renderer.WriteInfo("");
        renderer.WriteInfo($"Personas folder: {PersonaManager.Directory}");
        renderer.WriteInfo("Drop .md files there to add custom personas, or edit existing ones.");
        renderer.WriteInfo("");
        renderer.WriteInfo("Usage: /persona <name>    Switch persona");
        renderer.WriteInfo("       /persona reload    Reload from disk");
    }
}
