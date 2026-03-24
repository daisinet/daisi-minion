using Daisi.Minion.Coding;

namespace Daisi.Minion.Modules;

/// <summary>
/// A dynamically compiled extension module for a minion.
/// Modules can extend the system prompt, add tools, hook pre/post processing,
/// and self-evaluate after task completion.
/// </summary>
public interface IMinionModule
{
    string Name { get; }
    string Description { get; }

    /// <summary>Initialize the module with the minion's context.</summary>
    void Initialize(MinionModuleContext context);

    /// <summary>Return text to append to the system prompt, or null.</summary>
    string? ExtendSystemPrompt();

    /// <summary>Return additional tools this module provides, or null.</summary>
    IEnumerable<IMinionTool>? GetTools();

    /// <summary>Transform user input before it reaches the model, or return null to pass through.</summary>
    string? PreProcess(string userInput);

    /// <summary>Transform model response before it's returned, or return null to pass through.</summary>
    string? PostProcess(string response);

    /// <summary>Evaluate the module's contribution after a task completes.</summary>
    Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome);
}
