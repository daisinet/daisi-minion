using Daisi.Minion.Benchmarks;
using Daisi.Minion.Config;
using Daisi.Minion.Evolution;
using Daisi.Minion.Modules;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

/// <summary>
/// /darwin — trigger an evolution run. Evaluates modules, picks the weakest,
/// evolves it, self-evaluates the result, and pushes if improved.
///
/// Usage:
///   /darwin                    — auto-pick weakest module and evolve it
///   /darwin &lt;module-name&gt;     — evolve a specific module
///   /darwin --list             — show module scores
///   /darwin --all              — evolve all modules below threshold
/// </summary>
public sealed class DarwinCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager,
    Func<string, int, Task> onGoalSet) : ISlashCommandHandler
{
    private readonly BenchmarkStore _benchmarks = new();

    public async Task HandleAsync(string args, CancellationToken ct)
    {
        if (args == "--list" || args == "-l")
        {
            ShowModuleScores();
            return;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            // Auto-pick: find the weakest module
            var target = FindWeakestModule();
            if (target == null)
            {
                renderer.WriteInfo("No modules with evaluation data. Run some tasks first, then try /darwin.");
                return;
            }
            args = target;
        }

        if (args == "--all")
        {
            await EvolveAllBelowThreshold(ct);
            return;
        }

        await EvolveModule(args.Trim(), ct);
    }

    private void ShowModuleScores()
    {
        var modulesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");
        if (!Directory.Exists(modulesDir))
        {
            renderer.WriteInfo("No modules found.");
            return;
        }

        renderer.WriteInfoHeader("Module Evaluation Scores");
        foreach (var dir in Directory.GetDirectories(modulesDir))
        {
            var name = Path.GetFileName(dir);
            var history = _benchmarks.GetHistory(name);
            if (history.Count == 0)
            {
                renderer.WriteInfo($"  {name,-25} no data");
                continue;
            }

            var avg = history.Average(h => h.Score);
            var last = history[^1];
            var trend = history.Count >= 2
                ? (last.Score > history[^2].Score ? "↑" : last.Score < history[^2].Score ? "↓" : "→")
                : " ";
            renderer.WriteInfo($"  {name,-25} avg={avg:F2}  last={last.Score:F2} {trend}  ({history.Count} evals)");
        }
    }

    private string? FindWeakestModule()
    {
        var modulesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");
        if (!Directory.Exists(modulesDir)) return null;

        string? weakest = null;
        double weakestScore = double.MaxValue;

        foreach (var dir in Directory.GetDirectories(modulesDir))
        {
            var name = Path.GetFileName(dir);
            var history = _benchmarks.GetHistory(name);
            if (history.Count == 0) continue;

            var avg = history.Average(h => h.Score);
            if (avg < weakestScore)
            {
                weakestScore = avg;
                weakest = name;
            }
        }

        return weakest;
    }

    private async Task EvolveModule(string moduleName, CancellationToken ct)
    {
        var config = configManager.Config;
        var hasDaisiGit = !string.IsNullOrEmpty(config.DaisiGitServer)
            && !string.IsNullOrEmpty(config.DaisiGitToken)
            && !string.IsNullOrEmpty(config.ModulesRepo);

        renderer.WriteInfoHeader($"Darwin: evolving {moduleName}");

        // Build the evolution goal
        var currentScore = _benchmarks.GetAverageScore(moduleName);
        var history = _benchmarks.GetHistory(moduleName);
        var scoreInfo = history.Count > 0
            ? $"Current average score: {currentScore:F2} across {history.Count} evaluations."
            : "No evaluation data yet.";

        var goal = $"""
            Evolve the module '{moduleName}'. {scoreInfo}

            Steps:
            1. Read the current module source with read_module
            2. Analyze its strengths and weaknesses
            3. Create an improved version with create_module (compile + test it)
            4. Self-evaluate with evaluate_module — only proceed if verdict is ACCEPT
            5. If accepted, commit with commit_module
            {(hasDaisiGit ? "6. Push to remote with push_module" : "")}

            If evaluate_module returns REJECT, iterate: fix the issues and try again.
            Do NOT commit a version that fails evaluation.

            Focus on improving the module's effectiveness. Consider:
            - Better system prompt extensions
            - Smarter pre/post processing
            - More useful tools
            - Better self-evaluation logic
            """;

        await onGoalSet(goal, 10);
    }

    private async Task EvolveAllBelowThreshold(CancellationToken ct)
    {
        const double threshold = 0.6;
        var modulesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");
        if (!Directory.Exists(modulesDir)) return;

        var targets = new List<string>();
        foreach (var dir in Directory.GetDirectories(modulesDir))
        {
            var name = Path.GetFileName(dir);
            var avg = _benchmarks.GetAverageScore(name);
            if (avg > 0 && avg < threshold)
                targets.Add(name);
        }

        if (targets.Count == 0)
        {
            renderer.WriteInfo($"All modules score above {threshold:F1}. Nothing to evolve.");
            return;
        }

        renderer.WriteInfoHeader($"Darwin: evolving {targets.Count} module(s) below {threshold:F1}");
        foreach (var name in targets)
        {
            await EvolveModule(name, ct);
        }
    }
}
