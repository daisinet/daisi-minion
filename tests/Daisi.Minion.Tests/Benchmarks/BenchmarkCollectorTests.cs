using Daisi.Minion.Benchmarks;

namespace Daisi.Minion.Tests.Benchmarks;

public class BenchmarkCollectorTests
{
    [Fact]
    public void BuildOutcome_CapturesAllMetrics()
    {
        var collector = new BenchmarkCollector();
        collector.Start();
        collector.RecordTokens(100);
        collector.RecordTokens(200);
        collector.IncrementIteration();
        collector.IncrementIteration();
        collector.RecordToolCall();
        collector.RecordToolCall();
        collector.RecordToolCall();
        collector.RecordContextUtilization(0.5);
        collector.RecordContextUtilization(0.7); // peak
        collector.RecordContextUtilization(0.3);
        collector.RecordSuccess(true);
        collector.Stop();

        var outcome = collector.BuildOutcome("test task", 10);

        Assert.Equal(300, outcome.TotalTokens);
        Assert.Equal(2, outcome.IterationsUsed);
        Assert.Equal(3, outcome.ToolCalls);
        Assert.Equal(0.7, outcome.ContextUtilization);
        Assert.True(outcome.Succeeded);
        Assert.Equal("test task", outcome.TaskDescription);
        Assert.Equal(10, outcome.IterationBudget);
        Assert.True(outcome.Duration.TotalMilliseconds > 0);
    }

    [Fact]
    public void BuildOutcome_DefaultsToFailed()
    {
        var collector = new BenchmarkCollector();
        var outcome = collector.BuildOutcome();

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, outcome.TotalTokens);
        Assert.Equal(0, outcome.IterationsUsed);
    }
}
