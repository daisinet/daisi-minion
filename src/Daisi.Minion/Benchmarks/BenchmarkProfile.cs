using System.Text.Json;
using System.Text.Json.Serialization;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Benchmarks;

/// <summary>
/// Weighted scoring profile for benchmarks. Negative weights mean lower is better,
/// positive weights mean higher is better. Weights are normalized during scoring.
///
/// Core tenant: TestPassRate and RegressionPassRate can never be weighted below +1.0.
/// </summary>
public sealed class BenchmarkProfile
{
    [JsonPropertyName("tokens_per_task")]
    public double TokensPerTask { get; set; } = -0.3;

    [JsonPropertyName("iterations_to_completion")]
    public double IterationsToCompletion { get; set; } = -0.5;

    [JsonPropertyName("tool_calls_per_task")]
    public double ToolCallsPerTask { get; set; } = -0.15;

    [JsonPropertyName("context_utilization")]
    public double ContextUtilization { get; set; } = -0.3;

    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; } = -0.2;

    [JsonPropertyName("test_pass_rate")]
    public double TestPassRate { get; set; } = 1.0;

    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; set; } = 1.0;

    /// <summary>
    /// Score a task outcome against this profile. Returns a value from 0.0 to 1.0.
    /// </summary>
    public double Score(TaskOutcome outcome, TaskOutcome? baseline = null)
    {
        // Normalize metrics to 0-1 range using simple heuristics
        double tokenScore = Normalize(outcome.TotalTokens, 0, 8000);
        double iterScore = Normalize(outcome.IterationsUsed, 0, outcome.IterationBudget > 0 ? outcome.IterationBudget : 20);
        double toolScore = Normalize(outcome.ToolCalls, 0, 50);
        double ctxScore = outcome.ContextUtilization;
        double durationScore = Normalize(outcome.Duration.TotalSeconds, 0, 300);
        double testScore = outcome.TestsPass == true ? 1.0 : outcome.TestsPass == false ? 0.0 : 0.5;
        double successScore = outcome.Succeeded ? 1.0 : 0.0;

        // Apply weights (negative weight inverts: lower raw = higher score)
        double weighted = 0;
        double totalAbsWeight = 0;

        weighted += Apply(tokenScore, TokensPerTask, ref totalAbsWeight);
        weighted += Apply(iterScore, IterationsToCompletion, ref totalAbsWeight);
        weighted += Apply(toolScore, ToolCallsPerTask, ref totalAbsWeight);
        weighted += Apply(ctxScore, ContextUtilization, ref totalAbsWeight);
        weighted += Apply(durationScore, DurationSeconds, ref totalAbsWeight);
        weighted += Apply(testScore, TestPassRate, ref totalAbsWeight);
        weighted += Apply(successScore, SuccessRate, ref totalAbsWeight);

        return totalAbsWeight > 0 ? Math.Clamp(weighted / totalAbsWeight, 0, 1) : 0;
    }

    private static double Apply(double normalizedValue, double weight, ref double totalAbsWeight)
    {
        var absWeight = Math.Abs(weight);
        totalAbsWeight += absWeight;
        // Negative weight: lower value = higher score (invert)
        var score = weight < 0 ? (1.0 - normalizedValue) : normalizedValue;
        return score * absWeight;
    }

    private static double Normalize(double value, double min, double max) =>
        max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static BenchmarkProfile? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<BenchmarkProfile>(File.ReadAllText(path), JsonOpts) : null;

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
}
