using System.Text;
using System.Text.Json.Nodes;
using Daisi.Minion.Benchmarks;
using Daisi.Minion.Coding;

namespace Daisi.Minion.Orchestration;

/// <summary>
/// Summoner tool to evaluate a minion's completed work.
/// The summoner (overlord) is the objective evaluator — it assigned the task,
/// it knows the acceptance criteria, and it can verify the result.
///
/// The summoner provides:
/// - A score (0.0-1.0) based on task completion quality
/// - Optional notes explaining the rating
/// - Optional verification results (did the code compile, do tests pass)
///
/// The score is recorded against all modules that were active on the minion,
/// building the dataset Darwin uses to evolve modules.
/// </summary>
public sealed class EvaluateMinionTool : IMinionTool
{
    private readonly MinionPool _pool;
    private readonly BenchmarkStore _benchmarks = new();
    private readonly BenchmarkProfile _profile = new();

    public EvaluateMinionTool(MinionPool pool) => _pool = pool;

    public string Name => "evaluate_minion";
    public string Description =>
        "Evaluate a minion's completed work. Provide a quality score (0.0-1.0) and notes. " +
        "The score is recorded against the minion's active modules for Darwin evolution. " +
        "Call this after checking the minion's output to judge whether the task was done well.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["minion_id"] = new JsonObject { ["type"] = "string", ["description"] = "ID of the minion to evaluate" },
            ["score"] = new JsonObject { ["type"] = "number", ["description"] = "Quality score from 0.0 (terrible) to 1.0 (excellent)" },
            ["notes"] = new JsonObject { ["type"] = "string", ["description"] = "What was good/bad about the work" },
            ["compile_ok"] = new JsonObject { ["type"] = "boolean", ["description"] = "Did the code compile? (optional)" },
            ["tests_pass"] = new JsonObject { ["type"] = "boolean", ["description"] = "Do tests pass? (optional)" },
        },
        ["required"] = new JsonArray("minion_id", "score"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var minionId = ToolArgs.GetString(arguments, "minion_id");
        if (string.IsNullOrEmpty(minionId))
            return Task.FromResult(ToolResult.Error("Missing: minion_id"));

        if (!_pool.Children.TryGetValue(minionId, out var child))
            return Task.FromResult(ToolResult.Error($"Unknown minion: {minionId}"));

        // Hard gate: reject evaluation if the minion never did any work
        if (child.IterationCount == 0)
            return Task.FromResult(ToolResult.Error(
                $"Cannot evaluate {minionId}: 0 iterations completed. Send it a message first."));
        if (child.Status == ChildMinionStatus.Idle && child.ToolCallCount == 0)
            return Task.FromResult(ToolResult.Error(
                $"Cannot evaluate {minionId}: still idle with 0 tool calls. The minion hasn't done any work yet."));

        // Parse score — handle both number and string values from model output
        var scoreNode = arguments["score"];
        if (scoreNode == null)
            return Task.FromResult(ToolResult.Error("Missing: score"));
        double score;
        try
        {
            score = scoreNode.GetValueKind() == System.Text.Json.JsonValueKind.Number
                ? scoreNode.GetValue<double>()
                : double.Parse(scoreNode.GetValue<string>());
        }
        catch
        {
            return Task.FromResult(ToolResult.Error($"Invalid score: {scoreNode}"));
        }
        score = Math.Clamp(score, 0, 1);

        var notes = ToolArgs.GetString(arguments, "notes");

        // Store evaluation on the child
        child.EvaluationScore = score;
        child.EvaluationNotes = notes;

        // Build outcome from child metrics
        var outcome = child.BuildOutcome();

        // Overlay verification signals if provided
        if (arguments.ContainsKey("compile_ok"))
            outcome.CompileSuccess = arguments["compile_ok"]!.GetValue<bool>();
        if (arguments.ContainsKey("tests_pass"))
            outcome.TestsPass = arguments["tests_pass"]!.GetValue<bool>();

        // Compute the profile-weighted score (blends objective metrics with summoner's judgment)
        // Use summoner score as the primary signal, weighted with task metrics
        var profileScore = _profile.Score(outcome);
        var blendedScore = (score * 0.7) + (profileScore * 0.3); // summoner judgment dominates

        // Record against each active module
        var recorded = new StringBuilder();
        if (child.ActiveModules.Count > 0)
        {
            foreach (var moduleName in child.ActiveModules)
            {
                _benchmarks.Record(moduleName, outcome, blendedScore);
                recorded.AppendLine($"  {moduleName}: {blendedScore:F2}");
            }
        }
        else
        {
            // No modules active — record under a "_baseline" key
            _benchmarks.Record("_baseline", outcome, blendedScore);
            recorded.AppendLine($"  _baseline: {blendedScore:F2}");
        }

        var result = new StringBuilder();
        result.AppendLine($"Evaluated {minionId}: summoner={score:F2} profile={profileScore:F2} blended={blendedScore:F2}");
        if (notes != null) result.AppendLine($"Notes: {notes}");
        result.AppendLine("Recorded scores:");
        result.Append(recorded);

        return Task.FromResult(ToolResult.Success(result.ToString()));
    }
}
