using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

public class ModuleCompilerTests
{
    private readonly ModuleCompiler _compiler = new();

    private const string ValidModuleSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Daisi.Minion.Modules;
        using Daisi.Minion.Coding;

        public class TestModule : IMinionModule
        {
            public string Name => "test-module";
            public string Description => "A test module";
            public void Initialize(MinionModuleContext context) { }
            public string? ExtendSystemPrompt() => "Test extension";
            public IEnumerable<IMinionTool>? GetTools() => null;
            public string? PreProcess(string input) => null;
            public string? PostProcess(string response) => null;
            public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                Task.FromResult(new ModuleEvaluation { Score = 1.0 });
        }
        """;

    [Fact]
    public void CompileFromSource_ValidModule_Succeeds()
    {
        var result = _compiler.CompileFromSource(ValidModuleSource);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(result.Modules);
        Assert.Equal("test-module", result.Modules[0].Name);
    }

    [Fact]
    public void CompileFromSource_ValidModule_ExtendSystemPrompt()
    {
        var result = _compiler.CompileFromSource(ValidModuleSource);
        Assert.True(result.Success);
        Assert.Equal("Test extension", result.Modules[0].ExtendSystemPrompt());
    }

    [Fact]
    public void CompileFromSource_ForbiddenApi_Fails()
    {
        var source = """
            using System.IO;
            using Daisi.Minion.Modules;
            using Daisi.Minion.Coding;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public class BadModule : IMinionModule
            {
                public string Name => "bad";
                public string Description => "bad";
                public void Initialize(MinionModuleContext context) { }
                public string? ExtendSystemPrompt() => null;
                public IEnumerable<IMinionTool>? GetTools() => null;
                public string? PreProcess(string input) => null;
                public string? PostProcess(string response) => null;
                public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                    Task.FromResult(new ModuleEvaluation { Score = 0.0 });
            }
            """;

        var result = _compiler.CompileFromSource(source);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("System.IO"));
    }

    [Fact]
    public void CompileFromSource_SyntaxError_Fails()
    {
        var source = "public class Broken { this is not valid C# }";
        var result = _compiler.CompileFromSource(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void CompileFromSource_NoModuleInterface_Fails()
    {
        var source = """
            public class NotAModule
            {
                public string Name => "nope";
            }
            """;

        var result = _compiler.CompileFromSource(source);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("No IMinionModule"));
    }

    [Fact]
    public void CompileFromSource_LoadsInIsolatedContext()
    {
        var result = _compiler.CompileFromSource(ValidModuleSource);
        Assert.True(result.Success);
        Assert.NotNull(result.LoadContext);
    }
}
