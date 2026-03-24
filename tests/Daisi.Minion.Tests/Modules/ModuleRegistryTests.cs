using Daisi.Minion.Coding;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

public class ModuleRegistryTests
{
    [Fact]
    public void ExtendSystemPrompt_NoModules_ReturnsBase()
    {
        var registry = new ModuleRegistry();
        var result = registry.ExtendSystemPrompt("Base prompt");
        Assert.Equal("Base prompt", result);
    }

    [Fact]
    public void ExtendSystemPrompt_WithModules_AppendsExtensions()
    {
        var registry = new ModuleRegistry();
        registry.Add(new FakeModule("mod1", "Extension 1"));
        registry.Add(new FakeModule("mod2", "Extension 2"));

        var result = registry.ExtendSystemPrompt("Base prompt");
        Assert.Contains("Base prompt", result);
        Assert.Contains("Active Modules", result);
        Assert.Contains("Extension 1", result);
        Assert.Contains("Extension 2", result);
    }

    [Fact]
    public void PreProcess_ChainsModules()
    {
        var registry = new ModuleRegistry();
        registry.Add(new FakeModule("mod1", preProcess: input => input + " [mod1]"));
        registry.Add(new FakeModule("mod2", preProcess: input => input + " [mod2]"));

        var result = registry.PreProcess("hello");
        Assert.Equal("hello [mod1] [mod2]", result);
    }

    [Fact]
    public void PostProcess_ChainsModules()
    {
        var registry = new ModuleRegistry();
        registry.Add(new FakeModule("mod1", postProcess: r => r.ToUpperInvariant()));

        var result = registry.PostProcess("hello world");
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void PreProcess_NullReturn_PassesThrough()
    {
        var registry = new ModuleRegistry();
        registry.Add(new FakeModule("mod1")); // returns null for pre/post

        var result = registry.PreProcess("unchanged");
        Assert.Equal("unchanged", result);
    }

    [Fact]
    public async Task EvaluateAll_ReturnsAllEvaluations()
    {
        var registry = new ModuleRegistry();
        registry.Add(new FakeModule("mod1"));
        registry.Add(new FakeModule("mod2"));

        var outcome = new TaskOutcome { Succeeded = true };
        var evals = await registry.EvaluateAllAsync(outcome);

        Assert.Equal(2, evals.Count);
        Assert.Equal("mod1", evals[0].ModuleName);
        Assert.Equal("mod2", evals[1].ModuleName);
    }

    /// <summary>Simple fake module for testing the registry.</summary>
    private class FakeModule : IMinionModule
    {
        private readonly string? _promptExtension;
        private readonly Func<string, string?>? _preProcess;
        private readonly Func<string, string?>? _postProcess;

        public string Name { get; }
        public string Description => "Fake module for testing";

        public FakeModule(string name, string? promptExtension = null,
            Func<string, string?>? preProcess = null,
            Func<string, string?>? postProcess = null)
        {
            Name = name;
            _promptExtension = promptExtension;
            _preProcess = preProcess;
            _postProcess = postProcess;
        }

        public void Initialize(MinionModuleContext context) { }
        public string? ExtendSystemPrompt() => _promptExtension;
        public IEnumerable<IMinionTool>? GetTools() => null;
        public string? PreProcess(string input) => _preProcess?.Invoke(input);
        public string? PostProcess(string response) => _postProcess?.Invoke(response);
        public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
            Task.FromResult(new ModuleEvaluation { Score = 0.8 });
    }
}
