using Daisi.Minion.Config;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class BackendCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager,
    Action<string> onBackendChanged) : ISlashCommandHandler
{
    private static readonly string[] ValidBackends = ["auto", "cpu", "cuda", "vulkan", "daisinet"];

    public Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            var current = configManager.Config.Backend;
            renderer.WriteInfoHeader("Available backends:");
            foreach (var b in ValidBackends)
            {
                var marker = string.Equals(b, current, StringComparison.OrdinalIgnoreCase)
                    ? " \x1b[32m(active)\x1b[0m" : "";
                renderer.WriteInfo($"  {b}{marker}");
            }
            renderer.WriteInfo("");
            renderer.WriteInfo("Usage: /backend <name>");
            renderer.WriteInfo("Changing backend requires reloading the model.");
            return Task.CompletedTask;
        }

        var name = args.Trim().ToLowerInvariant();
        if (!ValidBackends.Contains(name))
        {
            renderer.WriteError($"Unknown backend: {name}");
            renderer.WriteInfo($"Available: {string.Join(", ", ValidBackends)}");
            return Task.CompletedTask;
        }

        configManager.Config.Backend = name;
        configManager.Save();

        renderer.WriteSuccess($"Backend set to: {name}");
        renderer.WriteInfo("Reloading model...");

        onBackendChanged(name);

        return Task.CompletedTask;
    }
}
