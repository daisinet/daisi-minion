using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Compiles and runs a module's co-located test file (tests.cs) via Roslyn.
/// Tests are discovered by convention: public async methods returning Task
/// whose names start with "Test" or have [Fact]-like attributes.
/// </summary>
public sealed class ModuleTestRunner
{
    /// <summary>
    /// Compile and run tests for a module.
    /// Returns the test results.
    /// </summary>
    public TestRunResult RunTests(string moduleSource, string testSource, int timeoutSeconds = 30)
    {
        // Compile module + tests together
        var moduleSyntax = CSharpSyntaxTree.ParseText(moduleSource, path: "module.cs");
        var testSyntax = CSharpSyntaxTree.ParseText(testSource, path: "tests.cs");

        // Safety check on both files
        var moduleViolations = SafetyAnalyzer.Analyze(moduleSyntax);
        var testViolations = SafetyAnalyzer.Analyze(testSyntax);
        if (moduleViolations.Count > 0 || testViolations.Count > 0)
        {
            var errors = moduleViolations.Concat(testViolations)
                .Select(v => v.GetMessage()).ToList();
            return new TestRunResult
            {
                Success = false,
                CompileErrors = errors,
            };
        }

        var references = BuildReferences();
        var assemblyName = $"ModuleTest_{Guid.NewGuid():N}";

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [moduleSyntax, testSyntax],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(false));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            return new TestRunResult
            {
                Success = false,
                CompileErrors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.GetMessage()).ToList(),
            };
        }

        // Load and discover test methods
        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(ms);
            return ExecuteTests(assembly, timeoutSeconds);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static TestRunResult ExecuteTests(Assembly assembly, int timeoutSeconds)
    {
        var results = new List<TestCaseResult>();
        var testMethods = DiscoverTestMethods(assembly);

        if (testMethods.Count == 0)
        {
            return new TestRunResult
            {
                Success = false,
                CompileErrors = ["No test methods found. Methods must be public, return Task, and start with 'Test'."],
            };
        }

        foreach (var (type, method) in testMethods)
        {
            var testCase = new TestCaseResult { Name = $"{type.Name}.{method.Name}" };

            try
            {
                var instance = Activator.CreateInstance(type);
                var task = method.Invoke(instance, null) as Task;

                if (task != null)
                {
                    var completed = task.Wait(TimeSpan.FromSeconds(timeoutSeconds));
                    if (!completed)
                    {
                        testCase.Passed = false;
                        testCase.Error = $"Test timed out after {timeoutSeconds}s";
                    }
                    else if (task.IsFaulted)
                    {
                        testCase.Passed = false;
                        testCase.Error = task.Exception?.InnerException?.Message ?? "Test faulted";
                    }
                    else
                    {
                        testCase.Passed = true;
                    }
                }
                else
                {
                    // Synchronous test — if it didn't throw, it passed
                    testCase.Passed = true;
                }
            }
            catch (TargetInvocationException ex)
            {
                testCase.Passed = false;
                testCase.Error = ex.InnerException?.Message ?? ex.Message;
            }
            catch (Exception ex)
            {
                testCase.Passed = false;
                testCase.Error = ex.Message;
            }

            results.Add(testCase);
        }

        return new TestRunResult
        {
            Success = results.All(r => r.Passed),
            TestCases = results,
        };
    }

    private static List<(Type Type, MethodInfo Method)> DiscoverTestMethods(Assembly assembly)
    {
        var methods = new List<(Type, MethodInfo)>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                // Convention: public methods starting with "Test" that return Task or void
                if (method.Name.StartsWith("Test", StringComparison.Ordinal) &&
                    method.GetParameters().Length == 0 &&
                    (method.ReturnType == typeof(Task) || method.ReturnType == typeof(void)))
                {
                    methods.Add((type, method));
                }
            }
        }

        return methods;
    }

    private static List<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (trustedAssemblies != null)
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Runtime", "System.Collections", "System.Linq",
                "System.Text.Json", "System.Text.RegularExpressions",
                "System.Threading", "System.Threading.Tasks",
                "netstandard", "System.Private.CoreLib", "System.ObjectModel",
                "System.ComponentModel",
            };

            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (allowed.Contains(name))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(IMinionModule).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Coding.IMinionTool).Assembly.Location));

        return refs;
    }
}

public sealed class TestRunResult
{
    public bool Success { get; set; }
    public List<string> CompileErrors { get; set; } = [];
    public List<TestCaseResult> TestCases { get; set; } = [];

    public int Passed => TestCases.Count(t => t.Passed);
    public int Failed => TestCases.Count(t => !t.Passed);
    public double PassRate => TestCases.Count > 0 ? (double)Passed / TestCases.Count : 0;

    public string Summary()
    {
        if (CompileErrors.Count > 0)
            return $"Compile failed: {string.Join("; ", CompileErrors.Take(3))}";
        return $"{Passed}/{TestCases.Count} passed ({PassRate:P0})";
    }
}

public sealed class TestCaseResult
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string? Error { get; set; }
}
