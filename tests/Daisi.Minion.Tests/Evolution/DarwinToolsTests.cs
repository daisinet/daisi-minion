using System.Text.Json.Nodes;
using Daisi.Minion.Evolution;

namespace Daisi.Minion.Tests.Evolution;

public class DarwinToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModuleEvolver _evolver;

    private const string ValidModuleSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Daisi.Minion.Modules;
        using Daisi.Minion.Coding;

        public class ToolTestModule : IMinionModule
        {
            public string Name => "tool-test";
            public string Description => "Test module";
            public void Initialize(MinionModuleContext context) { }
            public string? ExtendSystemPrompt() => "test";
            public IEnumerable<IMinionTool>? GetTools() => null;
            public string? PreProcess(string input) => null;
            public string? PostProcess(string response) => null;
            public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                Task.FromResult(new ModuleEvaluation { Score = 1.0 });
        }
        """;

    public DarwinToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "darwin-tools-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _evolver = new ModuleEvolver(new EvolutionConfig { ModulesDirectory = _tempDir });
    }

    [Fact]
    public async Task CreateModuleTool_ValidSource_Creates()
    {
        var tool = new CreateModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "new-mod",
            ["module_source"] = ValidModuleSource,
        }, CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        Assert.Contains("Created module", result.Output);
    }

    [Fact]
    public async Task CreateModuleTool_CompileError_ReturnsError()
    {
        var tool = new CreateModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "bad",
            ["module_source"] = "not valid C#",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("compile", result.Output);
    }

    [Fact]
    public async Task CompileModuleTool_ValidSource_Succeeds()
    {
        var tool = new CompileModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "compile-test",
            ["module_source"] = ValidModuleSource,
        }, CancellationToken.None);

        Assert.False(result.IsError, result.Output);
    }

    [Fact]
    public async Task TestModuleTool_PassingTests_Succeeds()
    {
        var tool = new TestModuleTool();
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["module_source"] = ValidModuleSource,
            ["test_source"] = """
                using System.Threading.Tasks;
                public class Tests {
                    public Task TestPasses() => Task.CompletedTask;
                }
                """,
        }, CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        Assert.Contains("PASS", result.Output);
    }

    [Fact]
    public async Task TestModuleTool_FailingTests_ReturnsError()
    {
        var tool = new TestModuleTool();
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["module_source"] = ValidModuleSource,
            ["test_source"] = """
                using System.Threading.Tasks;
                public class Tests {
                    public Task TestFails() { throw new System.Exception("nope"); }
                }
                """,
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("FAIL", result.Output);
    }

    [Fact]
    public async Task ReadModuleTool_NonExistent_ReturnsError()
    {
        var tool = new ReadModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "nonexistent",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Output);
    }

    [Fact]
    public async Task ReadModuleTool_ExistingModule_ReturnsSource()
    {
        _evolver.Commit("readable", ValidModuleSource);

        var tool = new ReadModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "readable",
        }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("ToolTestModule", result.Output);
    }

    [Fact]
    public async Task CommitModuleTool_ValidSource_Commits()
    {
        var tool = new CommitModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "committed",
            ["module_source"] = ValidModuleSource,
        }, CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        Assert.True(File.Exists(Path.Combine(_tempDir, "committed", "module.cs")));
    }

    [Fact]
    public async Task CommitModuleTool_InvalidSource_Rejects()
    {
        var tool = new CommitModuleTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["name"] = "invalid",
            ["module_source"] = "not valid C#",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Validation failed", result.Output);
    }

    [Fact]
    public async Task ListModulesTool_EmptyDir_ReturnsMessage()
    {
        var tool = new ListModulesTool(_evolver);
        var result = await tool.ExecuteAsync(new JsonObject(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("No modules", result.Output);
    }

    [Fact]
    public void DarwinTypeConfig_Exists()
    {
        Assert.True(Daisi.Minion.Types.MinionTypeFactory.Exists("darwin"));
        var config = Daisi.Minion.Types.MinionTypeFactory.Get("darwin");
        Assert.Contains("Darwin", config.SystemPromptExtension!);
        Assert.NotNull(config.AllowedTools);
        Assert.Empty(config.AllowedTools);
    }

    [Fact]
    public void AllDarwinTools_HaveDistinctNames()
    {
        var tools = new Daisi.Minion.Coding.IMinionTool[]
        {
            new CreateModuleTool(_evolver),
            new CompileModuleTool(_evolver),
            new TestModuleTool(),
            new ReadModuleTool(_evolver),
            new CommitModuleTool(_evolver),
            new ListModulesTool(_evolver),
        };

        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(6, names.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
