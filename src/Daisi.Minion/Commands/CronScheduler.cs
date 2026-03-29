using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daisi.Minion.Commands;

/// <summary>
/// Simple cron-style scheduler for recurring commands.
/// Stores schedules in ~/.daisi-minion/cron.json with last/next run times.
/// Checks for due tasks on a timer and executes them via the command dispatcher.
/// </summary>
public sealed class CronScheduler : IDisposable
{
    private readonly SlashCommandDispatcher _dispatcher;
    private readonly Action<string> _log;
    private readonly string _cronPath;
    private readonly Timer _timer;
    private List<CronEntry> _entries = [];
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public CronScheduler(SlashCommandDispatcher dispatcher, Action<string>? log = null)
    {
        _dispatcher = dispatcher;
        _log = log ?? (_ => { });
        _cronPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "cron.json");
        Load();
        // Check every 60 seconds
        _timer = new Timer(CheckDueTasks, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Add a recurring command schedule.
    /// </summary>
    public void Add(string name, string command, TimeSpan interval)
    {
        var existing = _entries.FindIndex(e => e.Name == name);
        var entry = new CronEntry
        {
            Name = name,
            Command = command,
            IntervalSeconds = (int)interval.TotalSeconds,
            LastRunUtc = null,
            NextRunUtc = DateTime.UtcNow.Add(interval),
            Enabled = true,
        };

        if (existing >= 0)
            _entries[existing] = entry;
        else
            _entries.Add(entry);

        Save();
        _log($"[cron] Scheduled '{name}': {command} every {FormatInterval(interval)}");
    }

    /// <summary>
    /// Remove a scheduled command.
    /// </summary>
    public bool Remove(string name)
    {
        var removed = _entries.RemoveAll(e => e.Name == name) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>
    /// List all scheduled commands.
    /// </summary>
    public IReadOnlyList<CronEntry> List() => _entries.AsReadOnly();

    /// <summary>
    /// Run a scheduled command immediately (resets its timer).
    /// </summary>
    public async Task RunNowAsync(string name, CancellationToken ct)
    {
        var entry = _entries.FirstOrDefault(e => e.Name == name);
        if (entry == null) return;
        await ExecuteEntry(entry, ct);
    }

    private async void CheckDueTasks(object? state)
    {
        var now = DateTime.UtcNow;
        var due = _entries.Where(e => e.Enabled && e.NextRunUtc <= now).ToList();
        if (due.Count == 0) return;

        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(30)); // safety timeout

        foreach (var entry in due)
        {
            try
            {
                await ExecuteEntry(entry, _cts.Token);
            }
            catch (Exception ex)
            {
                _log($"[cron] Error running '{entry.Name}': {ex.Message}");
                entry.LastError = ex.Message;
            }
        }

        Save();
    }

    private async Task ExecuteEntry(CronEntry entry, CancellationToken ct)
    {
        _log($"[cron] Running '{entry.Name}': {entry.Command}");

        entry.LastRunUtc = DateTime.UtcNow;
        entry.RunCount++;

        var command = entry.Command;
        if (!command.StartsWith('/'))
            command = "/" + command;

        var result = await _dispatcher.ExecuteAsync(command, ct);
        if (!result)
            _log($"[cron] Unknown command: {entry.Command}");

        entry.NextRunUtc = DateTime.UtcNow.AddSeconds(entry.IntervalSeconds);
        entry.LastError = null;
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_cronPath)) return;
        try
        {
            var json = File.ReadAllText(_cronPath);
            _entries = JsonSerializer.Deserialize<List<CronEntry>>(json, JsonOpts) ?? [];
        }
        catch { _entries = []; }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_cronPath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(_cronPath, JsonSerializer.Serialize(_entries, JsonOpts));
    }

    public static TimeSpan ParseInterval(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (s.EndsWith("s") && int.TryParse(s[..^1], out var secs)) return TimeSpan.FromSeconds(secs);
        if (s.EndsWith("m") && int.TryParse(s[..^1], out var mins)) return TimeSpan.FromMinutes(mins);
        if (s.EndsWith("h") && int.TryParse(s[..^1], out var hrs)) return TimeSpan.FromHours(hrs);
        if (s.EndsWith("d") && int.TryParse(s[..^1], out var days)) return TimeSpan.FromDays(days);
        if (int.TryParse(s, out var defaultMins)) return TimeSpan.FromMinutes(defaultMins);
        throw new FormatException($"Invalid interval: {s}. Use 30s, 5m, 2h, 1d.");
    }

    public static string FormatInterval(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{ts.TotalDays:F0}d";
        if (ts.TotalHours >= 1) return $"{ts.TotalHours:F0}h";
        if (ts.TotalMinutes >= 1) return $"{ts.TotalMinutes:F0}m";
        return $"{ts.TotalSeconds:F0}s";
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cts?.Dispose();
    }
}

public sealed class CronEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public int IntervalSeconds { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public DateTime NextRunUtc { get; set; }
    public bool Enabled { get; set; } = true;
    public int RunCount { get; set; }
    public string? LastError { get; set; }
}
