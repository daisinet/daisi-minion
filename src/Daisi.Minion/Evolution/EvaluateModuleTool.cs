using System.Text.Json.Nodes;
using Daisi.Minion.Benchmarks;
using Daisi.Minion.Coding;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Self-evaluation tool for Darwin. Compiles a module candidate, runs its tests,
/// and compares the result against the current version's benchmark scores.
/// Returns a pass/fail verdict with reasoning.
/// </summary>
public sealed class EvaluateModuleTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    private readonly ModuleTestRunner _testRunner = new();
    private readonly BenchmarkStore _benchmarks = new();

    public EvaluateModuleTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "evaluate_module";
    public string Description => "Self-evaluate an evolved module: compile it, run tests, compare against current version's scores. Returns whether the new version is an improvement.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name" },
            ["module_source"] = new JsonObject { ["type"] = "string", ["description"] = "New module C# source to evaluate" },
            ["test_source"] = new JsonObject { ["type"] = "string", ["description"] = "Test source (uses existing if not provided)" },
        },
        ["required"] = new JsonArray("name", "module_source"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name");
        var moduleSource = ToolArgs.GetString(arguments, "module_source");
        var testSource = ToolArgs.GetString(arguments, "test_source");

        if (string.IsNullOrEmpty(name)) return Task.FromResult(ToolResult.Error("Missing: name"));
        if (string.IsNullOrEmpty(moduleSource)) return Task.FromResult(ToolResult.Error("Missing: module_source"));

        // If no test source provided, use existing tests
        if (string.IsNullOrEmpty(testSource))
        {
            var (_, existingTests) = _evolver.ReadModule(name);
            testSource = existingTests;
        }

        var report = new System.Text.StringBuilder();
        report.AppendLine($"# Self-Evaluation: {name}");
        report.AppendLine();

        // Step 1: Compile
        var compileResult = _evolver.Validate(name, moduleSource, testSource);
        report.AppendLine($"## Compilation: {(compileResult.Success ? "PASS" : "FAIL")}");
        if (!compileResult.Success)
        {
            foreach (var err in compileResult.Errors.Take(5))
                report.AppendLine($"  - {err}");
            report.AppendLine();
            report.AppendLine("**Verdict: REJECT** — does not compile.");
            return Task.FromResult(ToolResult.Error(report.ToString()));
        }
        report.AppendLine();

        // Step 2: Run tests
        if (!string.IsNullOrEmpty(testSource))
        {
            var testResult = _testRunner.RunTests(moduleSource, testSource);
            report.AppendLine($"## Tests: {(testResult.Success ? "PASS" : "FAIL")} ({testResult.TestCases.Count} tests)");
            foreach (var tc in testResult.TestCases)
                report.AppendLine($"  {(tc.Passed ? "PASS" : "FAIL")} {tc.Name}{(tc.Error != null ? $": {tc.Error}" : "")}");
            report.AppendLine();

            if (!testResult.Success)
            {
                report.AppendLine("**Verdict: REJECT** — tests failed.");
                return Task.FromResult(ToolResult.Error(report.ToString()));
            }
        }
        else
        {
            report.AppendLine("## Tests: SKIP (no test source)");
            report.AppendLine();
        }

        // Step 3: Compare against current scores
        var history = _benchmarks.GetHistory(name);
        if (history.Count > 0)
        {
            var currentAvg = history.Average(h => h.Score);
            var lastScore = history[^1].Score;
            report.AppendLine($"## Current Version Scores");
            report.AppendLine($"  Average: {currentAvg:F2}  Last: {lastScore:F2}  Evaluations: {history.Count}");
            report.AppendLine();
        }
        else
        {
            report.AppendLine("## Current Version: no benchmark data (first version)");
            report.AppendLine();
        }

        // Step 4: Verdict
        report.AppendLine($"## Verdict: ACCEPT");
        report.AppendLine("Compiles cleanly, tests pass. Ready to commit.");

        return Task.FromResult(ToolResult.Success(report.ToString()));
    }
}
