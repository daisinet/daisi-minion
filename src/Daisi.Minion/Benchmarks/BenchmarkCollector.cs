using System.Diagnostics;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Benchmarks;

/// <summary>
/// Collects performance metrics during an agentic task and produces a TaskOutcome.
/// Wrap this around the agentic loop to track tokens, iterations, tool calls, etc.
/// </summary>
public sealed class BenchmarkCollector
{
    private readonly Stopwatch _stopwatch = new();
    private int _totalTokens;
    private int _iterations;
    private int _toolCalls;
    private double _peakContextUtilization;
    private bool _succeeded;

    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();

    public void RecordTokens(int count) => _totalTokens += count;
    public void IncrementIteration() => _iterations++;
    public void RecordToolCall() => _toolCalls++;

    public void RecordContextUtilization(double utilization)
    {
        if (utilization > _peakContextUtilization)
            _peakContextUtilization = utilization;
    }

    public void RecordSuccess(bool succeeded) => _succeeded = succeeded;

    /// <summary>
    /// Build a TaskOutcome from the collected metrics.
    /// </summary>
    public TaskOutcome BuildOutcome(string? taskDescription = null, int iterationBudget = 20)
    {
        return new TaskOutcome
        {
            Succeeded = _succeeded,
            TotalTokens = _totalTokens,
            IterationsUsed = _iterations,
            IterationBudget = iterationBudget,
            ToolCalls = _toolCalls,
            ContextUtilization = _peakContextUtilization,
            Duration = _stopwatch.Elapsed,
            TaskDescription = taskDescription,
        };
    }
}
