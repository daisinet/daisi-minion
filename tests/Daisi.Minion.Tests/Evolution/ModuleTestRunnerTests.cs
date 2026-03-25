using Daisi.Minion.Evolution;

namespace Daisi.Minion.Tests.Evolution;

public class ModuleTestRunnerTests
{
    private readonly ModuleTestRunner _runner = new();

    private const string ValidModuleSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Daisi.Minion.Modules;
        using Daisi.Minion.Coding;

        public class TestableModule : IMinionModule
        {
            public string Name => "testable";
            public string Description => "A testable module";
            public void Initialize(MinionModuleContext context) { }
            public string? ExtendSystemPrompt() => "test extension";
            public IEnumerable<IMinionTool>? GetTools() => null;
            public string? PreProcess(string input) => null;
            public string? PostProcess(string response) => null;
            public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                Task.FromResult(new ModuleEvaluation { Score = 1.0 });
        }
        """;

    [Fact]
    public void RunTests_PassingTests_Succeeds()
    {
        var testSource = """
            using System.Threading.Tasks;

            public class ModuleTests
            {
                public Task TestModuleNameIsCorrect()
                {
                    var module = new TestableModule();
                    if (module.Name != "testable")
                        throw new System.Exception("Wrong name");
                    return Task.CompletedTask;
                }

                public Task TestPromptExtension()
                {
                    var module = new TestableModule();
                    if (module.ExtendSystemPrompt() != "test extension")
                        throw new System.Exception("Wrong prompt");
                    return Task.CompletedTask;
                }
            }
            """;

        var result = _runner.RunTests(ValidModuleSource, testSource);

        Assert.True(result.Success, result.Summary());
        Assert.Equal(2, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(1.0, result.PassRate);
    }

    [Fact]
    public void RunTests_FailingTest_Reports()
    {
        var testSource = """
            using System.Threading.Tasks;

            public class ModuleTests
            {
                public Task TestAlwaysFails()
                {
                    throw new System.Exception("Intentional failure");
                }
            }
            """;

        var result = _runner.RunTests(ValidModuleSource, testSource);

        Assert.False(result.Success);
        Assert.Equal(0, result.Passed);
        Assert.Equal(1, result.Failed);
        Assert.Contains(result.TestCases, t => t.Error!.Contains("Intentional failure"));
    }

    [Fact]
    public void RunTests_MixedResults_ReportsCorrectly()
    {
        var testSource = """
            using System.Threading.Tasks;

            public class ModuleTests
            {
                public Task TestPasses()
                {
                    return Task.CompletedTask;
                }

                public Task TestFails()
                {
                    throw new System.Exception("Nope");
                }
            }
            """;

        var result = _runner.RunTests(ValidModuleSource, testSource);

        Assert.False(result.Success);
        Assert.Equal(1, result.Passed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0.5, result.PassRate);
    }

    [Fact]
    public void RunTests_CompileError_ReportsError()
    {
        var testSource = "this is not valid C#";

        var result = _runner.RunTests(ValidModuleSource, testSource);

        Assert.False(result.Success);
        Assert.NotEmpty(result.CompileErrors);
    }

    [Fact]
    public void RunTests_ForbiddenApiInModule_Rejected()
    {
        var badModule = """
            using System.IO;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Daisi.Minion.Modules;
            using Daisi.Minion.Coding;

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
                    Task.FromResult(new ModuleEvaluation());
            }
            """;

        var testSource = """
            using System.Threading.Tasks;
            public class Tests { public Task TestNothing() => Task.CompletedTask; }
            """;

        var result = _runner.RunTests(badModule, testSource);

        Assert.False(result.Success);
        Assert.NotEmpty(result.CompileErrors);
        Assert.Contains(result.CompileErrors, e => e.Contains("System.IO"));
    }

    [Fact]
    public void RunTests_NoTestMethods_ReportsError()
    {
        var testSource = """
            public class EmptyTests
            {
                // No public Test* methods
                private void Helper() { }
            }
            """;

        var result = _runner.RunTests(ValidModuleSource, testSource);

        Assert.False(result.Success);
    }
}
