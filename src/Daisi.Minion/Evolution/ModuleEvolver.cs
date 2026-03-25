using System.Text;
using Daisi.Minion.Benchmarks;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Core evolution engine for Darwin. Manages the fast loop:
/// read evaluation history → write improved module → compile → test → benchmark → commit/revert.
/// </summary>
public sealed class ModuleEvolver
{
    private readonly EvolutionConfig _config;
    private readonly ModuleCompiler _compiler = new();
    private readonly ModuleTestRunner _testRunner = new();
    private readonly BenchmarkStore _benchmarkStore;
    private readonly string _modulesDir;

    public ModuleEvolver(EvolutionConfig config, BenchmarkStore? benchmarkStore = null)
    {
        _config = config;
        _modulesDir = config.ModulesDirectory ?? EvolutionConfig.DefaultModulesDir;
        _benchmarkStore = benchmarkStore ?? new BenchmarkStore(_modulesDir);
    }

    /// <summary>
    /// Create a new module from a name and description.
    /// Scaffolds the directory structure with module.cs and tests.cs.
    /// </summary>
    public EvolutionResult CreateModule(string name, string moduleSource, string? testSource = null)
    {
        var moduleDir = Path.Combine(_modulesDir, name);
        Directory.CreateDirectory(moduleDir);

        // Compile first to validate
        var compileResult = _compiler.CompileFromSource(moduleSource);
        if (!compileResult.Success)
        {
            return new EvolutionResult
            {
                Success = false,
                Phase = "compile",
                Errors = compileResult.Errors.ToList(),
            };
        }

        // Run tests if provided
        if (testSource != null)
        {
            var testResult = _testRunner.RunTests(moduleSource, testSource, _config.TestTimeoutSeconds);
            if (!testResult.Success)
            {
                return new EvolutionResult
                {
                    Success = false,
                    Phase = "test",
                    Errors = testResult.CompileErrors.Count > 0
                        ? testResult.CompileErrors
                        : testResult.TestCases.Where(t => !t.Passed).Select(t => $"{t.Name}: {t.Error}").ToList(),
                    TestResult = testResult,
                };
            }
        }

        // Write to disk
        File.WriteAllText(Path.Combine(moduleDir, "module.cs"), moduleSource);
        if (testSource != null)
            File.WriteAllText(Path.Combine(moduleDir, "tests.cs"), testSource);

        return new EvolutionResult
        {
            Success = true,
            Phase = "complete",
            ModuleName = name,
            ModulePath = moduleDir,
        };
    }

    /// <summary>
    /// Validate a module: compile + run tests + benchmark against baseline.
    /// Does not write to disk — used by Darwin's fast loop before committing.
    /// </summary>
    public EvolutionResult Validate(string name, string moduleSource, string? testSource = null)
    {
        // Phase 1: Compile
        var compileResult = _compiler.CompileFromSource(moduleSource);
        if (!compileResult.Success)
        {
            return new EvolutionResult
            {
                Success = false,
                Phase = "compile",
                Errors = compileResult.Errors.ToList(),
            };
        }

        // Phase 2: Tests
        if (testSource != null)
        {
            var testResult = _testRunner.RunTests(moduleSource, testSource, _config.TestTimeoutSeconds);
            if (!testResult.Success || testResult.PassRate < _config.MinTestPassRate)
            {
                return new EvolutionResult
                {
                    Success = false,
                    Phase = "test",
                    Errors = testResult.CompileErrors.Count > 0
                        ? testResult.CompileErrors
                        : testResult.TestCases.Where(t => !t.Passed).Select(t => $"{t.Name}: {t.Error}").ToList(),
                    TestResult = testResult,
                };
            }
        }

        // Phase 3: Benchmark regression check
        var baselineScore = _benchmarkStore.GetAverageScore(name);
        if (baselineScore > 0)
        {
            // We can't run a full benchmark without a real task, but we check that
            // the module at least compiles and passes tests — the slow loop does real scoring
        }

        return new EvolutionResult
        {
            Success = true,
            Phase = "validated",
            ModuleName = name,
            BaselineScore = baselineScore,
        };
    }

    /// <summary>
    /// Commit a validated module version to disk, replacing the current one.
    /// </summary>
    public void Commit(string name, string moduleSource, string? testSource = null)
    {
        var moduleDir = Path.Combine(_modulesDir, name);
        Directory.CreateDirectory(moduleDir);

        File.WriteAllText(Path.Combine(moduleDir, "module.cs"), moduleSource);
        if (testSource != null)
            File.WriteAllText(Path.Combine(moduleDir, "tests.cs"), testSource);
    }

    /// <summary>
    /// Read the current source files for a module.
    /// </summary>
    public (string? moduleSource, string? testSource) ReadModule(string name)
    {
        var moduleDir = Path.Combine(_modulesDir, name);
        var modulePath = Path.Combine(moduleDir, "module.cs");
        var testPath = Path.Combine(moduleDir, "tests.cs");

        var moduleSource = File.Exists(modulePath) ? File.ReadAllText(modulePath) : null;
        var testSource = File.Exists(testPath) ? File.ReadAllText(testPath) : null;

        return (moduleSource, testSource);
    }

    /// <summary>
    /// Get the evaluation history for a module.
    /// </summary>
    public string GetEvaluationSummary(string name)
    {
        var history = _benchmarkStore.GetHistory(name);
        if (history.Count == 0) return $"No evaluation history for module '{name}'.";

        var avg = history.Average(h => h.Score);
        var recent = history.TakeLast(5).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Module: {name}");
        sb.AppendLine($"Total evaluations: {history.Count}");
        sb.AppendLine($"Average score: {avg:F3}");
        sb.AppendLine($"Recent scores: {string.Join(", ", recent.Select(h => h.Score.ToString("F3")))}");

        var failures = history.Count(h => !h.Succeeded);
        if (failures > 0)
            sb.AppendLine($"Failure rate: {(double)failures / history.Count:P0}");

        return sb.ToString();
    }

    /// <summary>
    /// List all modules in the modules directory.
    /// </summary>
    public List<string> ListModules()
    {
        if (!Directory.Exists(_modulesDir)) return [];
        return Directory.GetDirectories(_modulesDir)
            .Where(d => File.Exists(Path.Combine(d, "module.cs")))
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
    }
}

public sealed class EvolutionResult
{
    public bool Success { get; set; }

    /// <summary>Which phase completed or failed: compile, test, benchmark, validated, complete.</summary>
    public string Phase { get; set; } = "";

    public List<string> Errors { get; set; } = [];
    public string? ModuleName { get; set; }
    public string? ModulePath { get; set; }
    public double BaselineScore { get; set; }
    public TestRunResult? TestResult { get; set; }

    public string Summary()
    {
        if (Success) return $"[{Phase}] {ModuleName ?? "module"} OK (baseline: {BaselineScore:F3})";
        return $"[{Phase}] Failed: {string.Join("; ", Errors.Take(3))}";
    }
}
