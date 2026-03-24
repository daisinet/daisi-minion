using Daisi.Minion.Modules;
using Microsoft.CodeAnalysis.CSharp;

namespace Daisi.Minion.Tests.Modules;

public class SafetyAnalyzerTests
{
    [Fact]
    public void Analyze_CleanModule_NoViolations()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Nodes;
            using Daisi.Minion.Modules;
            using Daisi.Minion.Coding;

            public class TestModule : IMinionModule
            {
                public string Name => "test";
                public string Description => "A test module";
                public void Initialize(MinionModuleContext context) { }
                public string? ExtendSystemPrompt() => "Test prompt extension";
                public IEnumerable<IMinionTool>? GetTools() => null;
                public string? PreProcess(string input) => null;
                public string? PostProcess(string response) => null;
                public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                    Task.FromResult(new ModuleEvaluation { Score = 1.0 });
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.Empty(violations);
    }

    [Fact]
    public void Analyze_UsingSystemIO_Rejected()
    {
        var source = """
            using System.IO;
            public class Bad { public void Foo() { File.Delete("x"); } }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.GetMessage().Contains("System.IO"));
    }

    [Fact]
    public void Analyze_UsingSystemDiagnostics_Rejected()
    {
        var source = """
            using System.Diagnostics;
            public class Bad { public void Foo() { Process.Start("cmd"); } }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.GetMessage().Contains("System.Diagnostics"));
    }

    [Fact]
    public void Analyze_UsingSystemNet_Rejected()
    {
        var source = """
            using System.Net.Http;
            public class Bad { }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Analyze_UsingReflection_Rejected()
    {
        var source = """
            using System.Reflection;
            public class Bad { }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Analyze_UnsafeClass_Rejected()
    {
        var source = """
            public unsafe class Bad { }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.GetMessage().Contains("Unsafe"));
    }

    [Fact]
    public void Analyze_ProcessReference_Rejected()
    {
        var source = """
            public class Bad
            {
                public void Foo()
                {
                    var p = new System.Diagnostics.Process();
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Analyze_AllowedNamespaces_Pass()
    {
        var source = """
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using System.Text;
            using System.Text.Json;
            using System.Text.RegularExpressions;
            using System.Threading.Tasks;
            public class Ok { }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var violations = SafetyAnalyzer.Analyze(tree);
        Assert.Empty(violations);
    }
}
