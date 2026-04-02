using System.Text;
using System.Text.Json;
using Daisi.Llogos.Chat;
using Daisi.Minion.Benchmarks;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Evolution;

/// <summary>
/// Exports conversation history as ChatML JSONL training data for LoRA fine-tuning.
/// Each exported session becomes one line: {"text": "&lt;ChatML conversation&gt;"}.
/// The llogos training pipeline auto-detects assistant turns and masks prompt tokens,
/// so only assistant completions are trained on.
/// </summary>
public sealed class TrainingDataExporter
{
    private readonly string _outputDir;

    public TrainingDataExporter(string outputDir)
    {
        _outputDir = outputDir;
    }

    /// <summary>
    /// Export a conversation as a single JSONL line appended to the corpus file.
    /// Returns the number of messages exported, or 0 if the session was filtered out.
    /// </summary>
    public int Export(IReadOnlyList<ChatMessage> history, TaskOutcome? outcome, double minQuality)
    {
        if (history == null || history.Count < 4) return 0;

        if (!PassesQualityFilter(outcome, minQuality))
            return 0;

        var chatml = RenderChatML(history);
        if (string.IsNullOrWhiteSpace(chatml)) return 0;

        Directory.CreateDirectory(_outputDir);
        var corpusPath = GetCorpusPath();
        var jsonLine = JsonSerializer.Serialize(new { text = chatml });
        File.AppendAllText(corpusPath, jsonLine + "\n");

        return history.Count;
    }

    public string GetCorpusPath() =>
        Path.Combine(_outputDir, $"corpus-{DateTime.UtcNow:yyyy-MM}.jsonl");

    /// <summary>Get all corpus files in the training directory.</summary>
    public string[] GetCorpusFiles()
    {
        if (!Directory.Exists(_outputDir)) return [];
        return Directory.GetFiles(_outputDir, "corpus-*.jsonl")
            .OrderBy(f => f).ToArray();
    }

    /// <summary>
    /// Merge all corpus files into a single training data file.
    /// Returns the merged file path and total line count.
    /// </summary>
    public (string path, int lineCount) MergeCorpus()
    {
        var mergedPath = Path.Combine(_outputDir, "merged-corpus.jsonl");
        var files = GetCorpusFiles();
        int count = 0;

        using var writer = new StreamWriter(mergedPath, append: false);
        foreach (var file in files)
        {
            if (Path.GetFullPath(file) == Path.GetFullPath(mergedPath)) continue;
            foreach (var line in File.ReadLines(file))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    writer.WriteLine(line);
                    count++;
                }
            }
        }

        return (mergedPath, count);
    }

    /// <summary>
    /// Apply quality filter based on TaskOutcome signals.
    /// Rejects failures, reverted sessions, stopped sessions, and low-scoring sessions.
    /// </summary>
    internal static bool PassesQualityFilter(TaskOutcome? outcome, double minQuality)
    {
        if (outcome == null) return false;
        if (!outcome.Succeeded) return false;
        if (outcome.UserReverted == true) return false;
        if (outcome.WasStopped) return false;
        if (outcome.TestsPass == false) return false;
        if (outcome.ContextUtilization > 0.9) return false;

        var profile = new BenchmarkProfile();
        var score = profile.Score(outcome);
        return score >= minQuality;
    }

    /// <summary>
    /// Render conversation history as ChatML text.
    /// Mirrors MinionChatRenderer.RenderChatML format exactly:
    /// tool results are grouped into user turns with &lt;tool_response&gt; tags.
    /// </summary>
    internal static string RenderChatML(IReadOnlyList<ChatMessage> history)
    {
        var sb = new StringBuilder();

        for (int i = 0; i < history.Count; i++)
        {
            var msg = history[i];
            switch (msg.Role)
            {
                case "system":
                case "user":
                    if (!string.IsNullOrEmpty(msg.Content))
                        sb.Append($"<|im_start|>{msg.Role}\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "assistant":
                    sb.Append("<|im_start|>assistant\n").Append(msg.Content).Append("<|im_end|>\n");
                    break;

                case "tool":
                    // Group consecutive tool results into one user turn,
                    // matching MinionChatRenderer.RenderToolResponseChatML exactly
                    bool isFirst = i == 0 || history[i - 1].Role != "tool";
                    bool isLast = i == history.Count - 1 || history[i + 1].Role != "tool";

                    if (isFirst) sb.Append("<|im_start|>user");
                    sb.Append("\n<tool_response>\n").Append(msg.Content).Append("\n</tool_response>");
                    if (isLast) sb.Append("<|im_end|>\n");
                    break;
            }
        }

        return sb.ToString();
    }
}
