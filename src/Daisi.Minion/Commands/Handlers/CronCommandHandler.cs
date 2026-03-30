using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

/// <summary>
/// /cron — manage recurring scheduled commands.
///
/// Usage:
///   /cron add &lt;name&gt; &lt;interval&gt; &lt;command&gt;   — schedule a recurring command
///   /cron remove &lt;name&gt;                       — remove a schedule
///   /cron list                                — show all schedules
///   /cron run &lt;name&gt;                          — run a schedule now
///
/// Examples:
///   /cron add nightly-darwin 24h /darwin
///   /cron add evolve-safety 12h /darwin safety-guard
///   /cron add quick-check 30m /goal Run unit tests and report results
///   /cron remove nightly-darwin
///   /cron list
/// </summary>
public sealed class CronCommandHandler(
    AnsiRenderer renderer,
    CronScheduler scheduler) : ISlashCommandHandler
{
    public async Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            ShowHelp();
            return;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.TrimEntries);
        var sub = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : "";

        switch (sub)
        {
            case "add":
                HandleAdd(rest);
                break;
            case "remove" or "rm":
                HandleRemove(rest);
                break;
            case "list" or "ls":
                HandleList();
                break;
            case "run":
                await HandleRun(rest, ct);
                break;
            default:
                renderer.WriteError($"Unknown subcommand: {sub}");
                ShowHelp();
                break;
        }
    }

    private void HandleAdd(string args)
    {
        // Parse: <name> <interval> <command...>
        var parts = args.Split(' ', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            renderer.WriteError("Usage: /cron add <name> <interval> <command>");
            renderer.WriteInfo("  Intervals: 30s, 5m, 2h, 12h, 1d, 7d");
            return;
        }

        var name = parts[0];
        TimeSpan interval;
        try { interval = CronScheduler.ParseInterval(parts[1]); }
        catch (FormatException ex)
        {
            renderer.WriteError(ex.Message);
            return;
        }

        var command = parts[2];
        scheduler.Add(name, command, interval);
        renderer.WriteSuccess($"Scheduled '{name}': {command} every {CronScheduler.FormatInterval(interval)}");
        renderer.WriteInfo($"  Next run: {DateTime.UtcNow.Add(interval):yyyy-MM-dd HH:mm} UTC");
    }

    private void HandleRemove(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            renderer.WriteError("Usage: /cron remove <name>");
            return;
        }

        if (scheduler.Remove(name.Trim()))
            renderer.WriteSuccess($"Removed schedule '{name.Trim()}'");
        else
            renderer.WriteError($"Schedule '{name.Trim()}' not found.");
    }

    private void HandleList()
    {
        var entries = scheduler.List();
        if (entries.Count == 0)
        {
            renderer.WriteInfo("No scheduled commands. Use /cron add to create one.");
            return;
        }

        renderer.WriteInfoHeader("Scheduled Commands");
        foreach (var e in entries)
        {
            var interval = CronScheduler.FormatInterval(TimeSpan.FromSeconds(e.IntervalSeconds));
            var status = e.Enabled ? "active" : "paused";
            var lastRun = e.LastRunUtc.HasValue ? e.LastRunUtc.Value.ToString("yyyy-MM-dd HH:mm") : "never";
            var nextRun = e.NextRunUtc.ToString("yyyy-MM-dd HH:mm");
            var error = e.LastError != null ? $" ⚠ {e.LastError}" : "";

            renderer.WriteInfo($"  {e.Name,-20} every {interval,-6} cmd={e.Command}");
            renderer.WriteInfo($"  {"",20} {status}  runs={e.RunCount}  last={lastRun}  next={nextRun}{error}");
        }
    }

    private async Task HandleRun(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            renderer.WriteError("Usage: /cron run <name>");
            return;
        }

        var entry = scheduler.List().FirstOrDefault(e => e.Name == name.Trim());
        if (entry == null)
        {
            renderer.WriteError($"Schedule '{name.Trim()}' not found.");
            return;
        }

        renderer.WriteInfo($"Running '{name.Trim()}' now...");
        await scheduler.RunNowAsync(name.Trim(), ct);
    }

    private void ShowHelp()
    {
        renderer.WriteInfoHeader("Cron — scheduled recurring commands");
        renderer.WriteInfo("Usage:");
        renderer.WriteInfo("  /cron add <name> <interval> <command>  — schedule a command");
        renderer.WriteInfo("  /cron remove <name>                    — remove a schedule");
        renderer.WriteInfo("  /cron list                             — show all schedules");
        renderer.WriteInfo("  /cron run <name>                       — run immediately");
        renderer.WriteInfo("");
        renderer.WriteInfo("Intervals: 30s, 5m, 1h, 12h, 1d, 7d");
        renderer.WriteInfo("");
        renderer.WriteInfo("Examples:");
        renderer.WriteInfo("  /cron add nightly-darwin 24h /darwin");
        renderer.WriteInfo("  /cron add evolve-all 12h /darwin --all");
    }
}
