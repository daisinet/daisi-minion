namespace Daisi.Minion.Modules;

/// <summary>
/// Captures the result of a completed task for evaluation and benchmarking.
/// </summary>
public sealed class TaskOutcome
{
    // Objective signals
    public bool Succeeded { get; set; }
    public bool? CompileSuccess { get; set; }
    public bool? TestsPass { get; set; }
    public int? TestsAdded { get; set; }
    public int IterationsUsed { get; set; }
    public int IterationBudget { get; set; }
    public double ContextUtilization { get; set; }
    public int TotalTokens { get; set; }
    public int ToolCalls { get; set; }

    // User signals
    public bool? UserApproved { get; set; }
    public bool? UserEdited { get; set; }
    public bool? UserReverted { get; set; }

    // Inferred signals
    public int FilesModified { get; set; }
    public bool WasStopped { get; set; }
    public TimeSpan Duration { get; set; }

    // Self-assessment
    public double SelfScore { get; set; }
    public string? SelfNotes { get; set; }

    // Task metadata
    public string? TaskDescription { get; set; }
    public string? MinionType { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
