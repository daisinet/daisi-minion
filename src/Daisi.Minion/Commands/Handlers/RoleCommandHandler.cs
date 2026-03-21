using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class RoleCommandHandler(
    AnsiRenderer renderer,
    RoleManager roles,
    ConfigManager configManager,
    Action onRoleChanged) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ListRoles();
            return Task.CompletedTask;
        }

        if (args.Trim().Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            roles.Reload();
            renderer.WriteSuccess($"Reloaded {roles.Available.Count} roles from disk.");
            return Task.CompletedTask;
        }

        var roleName = args.Trim();
        if (!roles.Exists(roleName))
        {
            renderer.WriteError($"Unknown role: {roleName}");
            renderer.WriteInfo($"Available: {string.Join(", ", roles.Available)}");
            return Task.CompletedTask;
        }

        configManager.Config.ActiveRole = roleName;
        configManager.Save();

        renderer.WriteSuccess($"Role set to: {roleName}");
        renderer.WriteInfo("Conversation reset with new role.");

        onRoleChanged();

        return Task.CompletedTask;
    }

    private void ListRoles()
    {
        var current = configManager.Config.ActiveRole;
        renderer.WriteInfoHeader("Available roles:");
        foreach (var name in roles.Available)
        {
            var marker = string.Equals(name, current, StringComparison.OrdinalIgnoreCase)
                ? " \x1b[32m(active)\x1b[0m" : "";
            renderer.WriteInfo($"  {name}{marker}");
        }
        renderer.WriteInfo("");
        renderer.WriteInfo($"Roles folder: {RoleManager.Directory}");
        renderer.WriteInfo("Drop .md files there to add custom roles, or edit existing ones.");
        renderer.WriteInfo("");
        renderer.WriteInfo("Usage: /role <name>     Switch role");
        renderer.WriteInfo("       /role reload     Reload from disk");
        renderer.WriteInfo("       Shift+Tab        Cycle through roles");
    }
}
