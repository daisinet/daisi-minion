using System.Text;
using Daisi.Minion.Coding;

namespace Daisi.Minion.Modules;

/// <summary>
/// Runtime registry of active modules. Aggregates tools, prompt extensions,
/// and pre/post processing hooks from all loaded modules.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly List<IMinionModule> _modules = [];

    public IReadOnlyList<IMinionModule> Modules => _modules;

    /// <summary>Add a module to the registry.</summary>
    public void Add(IMinionModule module) => _modules.Add(module);

    /// <summary>Add multiple modules to the registry.</summary>
    public void AddRange(IEnumerable<IMinionModule> modules) => _modules.AddRange(modules);

    /// <summary>Initialize all modules with the given context.</summary>
    public void InitializeAll(MinionModuleContext context)
    {
        foreach (var module in _modules)
            module.Initialize(context);
    }

    /// <summary>
    /// Get all tools provided by modules.
    /// </summary>
    public List<IMinionTool> GetAllTools()
    {
        var tools = new List<IMinionTool>();
        foreach (var module in _modules)
        {
            var moduleTools = module.GetTools();
            if (moduleTools != null)
                tools.AddRange(moduleTools);
        }
        return tools;
    }

    /// <summary>
    /// Extend a base system prompt with all module prompt extensions.
    /// </summary>
    public string ExtendSystemPrompt(string basePrompt)
    {
        var extensions = _modules
            .Select(m => m.ExtendSystemPrompt())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (extensions.Count == 0)
            return basePrompt;

        var sb = new StringBuilder(basePrompt);
        sb.AppendLine();
        sb.AppendLine("--- Active Modules ---");
        foreach (var ext in extensions)
        {
            sb.AppendLine();
            sb.AppendLine(ext);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Run all module preprocessors on user input. Returns transformed input.
    /// Modules are chained — each gets the output of the previous.
    /// </summary>
    public string PreProcess(string userInput)
    {
        var result = userInput;
        foreach (var module in _modules)
        {
            var transformed = module.PreProcess(result);
            if (transformed != null)
                result = transformed;
        }
        return result;
    }

    /// <summary>
    /// Run all module postprocessors on model response. Returns transformed response.
    /// </summary>
    public string PostProcess(string response)
    {
        var result = response;
        foreach (var module in _modules)
        {
            var transformed = module.PostProcess(result);
            if (transformed != null)
                result = transformed;
        }
        return result;
    }

    /// <summary>
    /// Evaluate all modules against a task outcome.
    /// </summary>
    public async Task<List<ModuleEvaluation>> EvaluateAllAsync(TaskOutcome outcome)
    {
        var evaluations = new List<ModuleEvaluation>();
        foreach (var module in _modules)
        {
            var eval = await module.EvaluateAsync(outcome);
            eval.ModuleName = module.Name;
            evaluations.Add(eval);
        }
        return evaluations;
    }
}
