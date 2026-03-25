using Daisi.Minion.Evolution;
using Daisi.Minion.Benchmarks;

namespace Daisi.Minion.Tests.Evolution;

public class ModuleEvolverTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModuleEvolver _evolver;

    private const string ValidModuleSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Daisi.Minion.Modules;
        using Daisi.Minion.Coding;

        public class EvolvedModule : IMinionModule
        {
            public string Name => "evolved";
            public string Description => "An evolved module";
            public void Initialize(MinionModuleContext context) { }
            public string? ExtendSystemPrompt() => "evolved prompt";
            public IEnumerable<IMinionTool>? GetTools() => null;
            public string? PreProcess(string input) => null;
            public string? PostProcess(string response) => null;
            public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                Task.FromResult(new ModuleEvaluation { Score = 0.9 });
        }
        """;

    private const string ValidTestSource = """
        using System.Threading.Tasks;

        public class EvolvedModuleTests
        {
            public Task TestNameIsCorrect()
            {
                var m = new EvolvedModule();
                if (m.Name != "evolved") throw new System.Exception("Wrong name");
                return Task.CompletedTask;
            }
        }
        """;

    public ModuleEvolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "evolver-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _evolver = new ModuleEvolver(new EvolutionConfig { ModulesDirectory = _tempDir });
    }

    [Fact]
    public void CreateModule_ValidSource_CreatesFiles()
    {
        var result = _evolver.CreateModule("test-mod", ValidModuleSource, ValidTestSource);

        Assert.True(result.Success, result.Summary());
        Assert.True(File.Exists(Path.Combine(_tempDir, "test-mod", "module.cs")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "test-mod", "tests.cs")));
    }

    [Fact]
    public void CreateModule_CompileError_Fails()
    {
        var result = _evolver.CreateModule("bad-mod", "not valid C#");

        Assert.False(result.Success);
        Assert.Equal("compile", result.Phase);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CreateModule_TestFailure_Fails()
    {
        var badTest = """
            using System.Threading.Tasks;
            public class Tests {
                public Task TestFails() { throw new System.Exception("nope"); }
            }
            """;

        var result = _evolver.CreateModule("fail-mod", ValidModuleSource, badTest);

        Assert.False(result.Success);
        Assert.Equal("test", result.Phase);
    }

    [Fact]
    public void Validate_ValidModule_Succeeds()
    {
        var result = _evolver.Validate("test-mod", ValidModuleSource, ValidTestSource);

        Assert.True(result.Success);
        Assert.Equal("validated", result.Phase);
    }

    [Fact]
    public void Validate_ForbiddenApi_Fails()
    {
        var bad = """
            using System.IO;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Daisi.Minion.Modules;
            using Daisi.Minion.Coding;
            public class Bad : IMinionModule {
                public string Name => "bad";
                public string Description => "bad";
                public void Initialize(MinionModuleContext c) {}
                public string? ExtendSystemPrompt() => null;
                public IEnumerable<IMinionTool>? GetTools() => null;
                public string? PreProcess(string i) => null;
                public string? PostProcess(string r) => null;
                public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome o) =>
                    Task.FromResult(new ModuleEvaluation());
            }
            """;

        var result = _evolver.Validate("bad", bad);
        Assert.False(result.Success);
        Assert.Equal("compile", result.Phase);
    }

    [Fact]
    public void Commit_WritesFiles()
    {
        _evolver.Commit("committed-mod", ValidModuleSource, ValidTestSource);

        Assert.True(File.Exists(Path.Combine(_tempDir, "committed-mod", "module.cs")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "committed-mod", "tests.cs")));
    }

    [Fact]
    public void ReadModule_ExistingModule_ReturnsSource()
    {
        _evolver.Commit("read-mod", ValidModuleSource, ValidTestSource);

        var (moduleSource, testSource) = _evolver.ReadModule("read-mod");

        Assert.NotNull(moduleSource);
        Assert.Contains("EvolvedModule", moduleSource);
        Assert.NotNull(testSource);
        Assert.Contains("TestNameIsCorrect", testSource);
    }

    [Fact]
    public void ReadModule_NonExistent_ReturnsNull()
    {
        var (moduleSource, testSource) = _evolver.ReadModule("nonexistent");
        Assert.Null(moduleSource);
        Assert.Null(testSource);
    }

    [Fact]
    public void ListModules_ReturnsModuleNames()
    {
        _evolver.Commit("mod-a", ValidModuleSource);
        _evolver.Commit("mod-b", ValidModuleSource);

        var modules = _evolver.ListModules();

        Assert.Equal(2, modules.Count);
        Assert.Contains("mod-a", modules);
        Assert.Contains("mod-b", modules);
    }

    [Fact]
    public void ListModules_EmptyDir_ReturnsEmpty()
    {
        Assert.Empty(_evolver.ListModules());
    }

    [Fact]
    public void GetEvaluationSummary_NoHistory_ReturnsMessage()
    {
        var summary = _evolver.GetEvaluationSummary("no-history");
        Assert.Contains("No evaluation history", summary);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
