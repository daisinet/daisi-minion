using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Configuration for Darwin's evolution loop.
/// </summary>
public sealed class EvolutionConfig
{
    /// <summary>Max iterations per fast-loop evolution cycle.</summary>
    [JsonPropertyName("max_iterations_per_cycle")]
    public int MaxIterationsPerCycle { get; set; } = 10;

    /// <summary>Minimum test pass rate to accept a module version (0.0 to 1.0).</summary>
    [JsonPropertyName("min_test_pass_rate")]
    public double MinTestPassRate { get; set; } = 1.0;

    /// <summary>Score regression threshold — if new score is more than this below baseline, reject.</summary>
    [JsonPropertyName("regression_threshold")]
    public double RegressionThreshold { get; set; } = 0.05;

    /// <summary>Timeout in seconds for running a single module test.</summary>
    [JsonPropertyName("test_timeout_seconds")]
    public int TestTimeoutSeconds { get; set; } = 30;

    /// <summary>Modules directory path.</summary>
    [JsonPropertyName("modules_directory")]
    public string? ModulesDirectory { get; set; }

    public static string DefaultModulesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");
}
