using System.Text.Json;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Benchmarks;

/// <summary>
/// Persists benchmark results per module. Stores scored outcomes in
/// ~/.daisi-minion/modules/{name}/evaluation.json as an append-only JSON array.
/// </summary>
public sealed class BenchmarkStore
{
    private static readonly string DefaultModulesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");

    private readonly string _modulesDir;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BenchmarkStore(string? modulesDir = null)
    {
        _modulesDir = modulesDir ?? DefaultModulesDir;
    }

    /// <summary>
    /// Record a benchmark entry for a module.
    /// </summary>
    public void Record(string moduleName, TaskOutcome outcome, double score)
    {
        var entry = new BenchmarkEntry
        {
            Score = score,
            Succeeded = outcome.Succeeded,
            TotalTokens = outcome.TotalTokens,
            IterationsUsed = outcome.IterationsUsed,
            ToolCalls = outcome.ToolCalls,
            ContextUtilization = outcome.ContextUtilization,
            DurationSeconds = outcome.Duration.TotalSeconds,
            TaskDescription = outcome.TaskDescription,
            RecordedAt = DateTime.UtcNow,
        };

        var dir = Path.Combine(_modulesDir, moduleName);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "evaluation.json");
        var entries = Load(path);
        entries.Add(entry);

        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOpts));
    }

    /// <summary>
    /// Get the evaluation history for a module.
    /// </summary>
    public List<BenchmarkEntry> GetHistory(string moduleName)
    {
        var path = Path.Combine(_modulesDir, moduleName, "evaluation.json");
        return Load(path);
    }

    /// <summary>
    /// Get the average score for a module across all recorded evaluations.
    /// </summary>
    public double GetAverageScore(string moduleName)
    {
        var history = GetHistory(moduleName);
        return history.Count > 0 ? history.Average(e => e.Score) : 0;
    }

    private static List<BenchmarkEntry> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<BenchmarkEntry>>(json, JsonOpts) ?? [];
        }
        catch { return []; }
    }
}

public sealed class BenchmarkEntry
{
    public double Score { get; set; }
    public bool Succeeded { get; set; }
    public int TotalTokens { get; set; }
    public int IterationsUsed { get; set; }
    public int ToolCalls { get; set; }
    public double ContextUtilization { get; set; }
    public double DurationSeconds { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime RecordedAt { get; set; }
}
