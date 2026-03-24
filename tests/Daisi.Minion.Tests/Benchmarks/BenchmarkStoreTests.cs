using Daisi.Minion.Benchmarks;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Benchmarks;

public class BenchmarkStoreTests : IDisposable
{
    private readonly string _tempDir;

    public BenchmarkStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "benchmark-store-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Record_CreatesEvaluationFile()
    {
        var store = new BenchmarkStore(_tempDir);
        var outcome = new TaskOutcome { Succeeded = true, TotalTokens = 500 };

        store.Record("test-module", outcome, 0.85);

        var path = Path.Combine(_tempDir, "test-module", "evaluation.json");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Record_AppendsMultipleEntries()
    {
        var store = new BenchmarkStore(_tempDir);

        store.Record("test-module", new TaskOutcome { Succeeded = true }, 0.8);
        store.Record("test-module", new TaskOutcome { Succeeded = false }, 0.3);
        store.Record("test-module", new TaskOutcome { Succeeded = true }, 0.9);

        var history = store.GetHistory("test-module");
        Assert.Equal(3, history.Count);
        Assert.Equal(0.8, history[0].Score);
        Assert.Equal(0.3, history[1].Score);
        Assert.Equal(0.9, history[2].Score);
    }

    [Fact]
    public void GetHistory_NonExistentModule_ReturnsEmpty()
    {
        var store = new BenchmarkStore(_tempDir);
        var history = store.GetHistory("nonexistent");
        Assert.Empty(history);
    }

    [Fact]
    public void GetAverageScore_CalculatesCorrectly()
    {
        var store = new BenchmarkStore(_tempDir);

        store.Record("avg-module", new TaskOutcome(), 0.6);
        store.Record("avg-module", new TaskOutcome(), 0.8);
        store.Record("avg-module", new TaskOutcome(), 1.0);

        var avg = store.GetAverageScore("avg-module");
        Assert.Equal(0.8, avg, precision: 5);
    }

    [Fact]
    public void GetAverageScore_NoEntries_ReturnsZero()
    {
        var store = new BenchmarkStore(_tempDir);
        Assert.Equal(0, store.GetAverageScore("empty-module"));
    }

    [Fact]
    public void Record_CapturesAllFields()
    {
        var store = new BenchmarkStore(_tempDir);
        var outcome = new TaskOutcome
        {
            Succeeded = true,
            TotalTokens = 1000,
            IterationsUsed = 5,
            ToolCalls = 12,
            ContextUtilization = 0.45,
            Duration = TimeSpan.FromSeconds(30),
            TaskDescription = "Fix the bug",
        };

        store.Record("detail-module", outcome, 0.75);

        var history = store.GetHistory("detail-module");
        Assert.Single(history);
        var entry = history[0];
        Assert.Equal(0.75, entry.Score);
        Assert.True(entry.Succeeded);
        Assert.Equal(1000, entry.TotalTokens);
        Assert.Equal(5, entry.IterationsUsed);
        Assert.Equal(12, entry.ToolCalls);
        Assert.Equal(0.45, entry.ContextUtilization);
        Assert.Equal(30, entry.DurationSeconds, precision: 1);
        Assert.Equal("Fix the bug", entry.TaskDescription);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
