namespace Daisi.Minion.Modules;

/// <summary>
/// A module's self-evaluation of its contribution to a completed task.
/// </summary>
public sealed class ModuleEvaluation
{
    /// <summary>Score from 0.0 (total failure) to 1.0 (perfect).</summary>
    public double Score { get; set; }

    /// <summary>Free-text notes about the evaluation.</summary>
    public string? Notes { get; set; }

    /// <summary>The module that produced this evaluation.</summary>
    public string? ModuleName { get; set; }

    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}
