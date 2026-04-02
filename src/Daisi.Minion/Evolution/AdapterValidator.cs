using Daisi.Minion.Benchmarks;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Validates a trained adapter by comparing benchmark scores before and after.
/// Used to decide whether to promote an adapter.
/// </summary>
public sealed class AdapterValidator
{
    private readonly BenchmarkProfile _profile = new();

    /// <summary>
    /// Compare a new outcome against a baseline. Returns true if the adapter should be promoted.
    /// Allows a small regression threshold to avoid rejecting adapters due to noise.
    /// </summary>
    public bool ShouldPromote(TaskOutcome withAdapter, TaskOutcome? baseline)
    {
        var newScore = _profile.Score(withAdapter);
        var baseScore = baseline != null ? _profile.Score(baseline) : 0;

        const double regressionThreshold = 0.05;
        return newScore >= baseScore - regressionThreshold;
    }

    public double Score(TaskOutcome outcome) => _profile.Score(outcome);
}
