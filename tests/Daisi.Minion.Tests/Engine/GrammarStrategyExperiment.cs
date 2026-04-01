using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;
using Daisi.Inference.Models;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Engine;
using ChatMessage = Daisi.Llogos.Chat.ChatMessage;

namespace Daisi.Minion.Tests.Engine;

/// <summary>
/// Experiment: compare grammar strategies for tool-call reliability.
///
/// Strategies:
///   A — Strict JSON: grammar forces raw JSON tool call, no tags, no thinking
///   B — Tag-wrapped: grammar forces &lt;tool_call&gt;{json}&lt;/tool_call&gt;
///   C — Baseline: no grammar (current production behavior)
///
/// Each strategy runs the same prompts. Results logged to JSON report.
/// </summary>
public class GrammarStrategyExperiment : IAsyncLifetime
{
    private static readonly string TestModelPath = @"C:\GGUFS\custom\Qwen3.5-9B-Q8_0.gguf";
    private static readonly string TestDir = Path.Combine(Path.GetTempPath(), $"minion-grammar-exp-{Guid.NewGuid():N}");
    private static readonly string ReportDir = @"C:\minion-dev\grammar-experiments";

    private DaisiLlogosModelHandle? _modelHandle;
    private bool _canRun;

    public async ValueTask InitializeAsync()
    {
        if (!File.Exists(TestModelPath)) return;
        try
        {
            var backend = new DaisiLlogosTextBackend();
            await backend.ConfigureAsync(new BackendConfiguration { Runtime = "auto" });
            var adapter = await backend.LoadModelAsync(new ModelLoadRequest
            {
                ModelId = "test-9b",
                FilePath = TestModelPath,
                ContextSize = 8192,
            });
            _modelHandle = ((DaisiLlogosModelHandleAdapter)adapter).Inner;
            Directory.CreateDirectory(TestDir);
            Directory.CreateDirectory(ReportDir);
            _canRun = true;
        }
        catch { }
    }

    public ValueTask DisposeAsync()
    {
        _modelHandle?.Dispose();
        if (Directory.Exists(TestDir))
            try { Directory.Delete(TestDir, true); } catch { }
        return ValueTask.CompletedTask;
    }

    private void Skip() { if (!_canRun) Assert.Skip("Model not available at " + TestModelPath); }

    // ══════════════════════════════════════════════════════════════════
    //  Test prompts
    // ══════════════════════════════════════════════════════════════════

    private static readonly (string Name, string Prompt, string ExpectedTool)[] TestPrompts =
    [
        ("simple_write",
            "Create a file called {DIR}/hello.txt with the content 'Hello, World!'. Use file_write.",
            "file_write"),
        ("html_page",
            "Create {DIR}/index.html with a basic HTML5 page containing <h1>Test</h1>. Use file_write.",
            "file_write"),
        ("read_file",
            "Read the file at {DIR}/hello.txt using file_read.",
            "file_read"),
        ("complex_html",
            "Create {DIR}/site.html — a Bootstrap 5 page with a navbar, hero titled 'Spacious', " +
            "and a contact form. Black and white. Use file_write with the complete HTML.",
            "file_write"),
    ];

    // ══════════════════════════════════════════════════════════════════
    //  The experiment
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunGrammarComparison()
    {
        Skip();

        var tools = new CodingToolRegistry();
        tools.Register(new FileWriteTool());
        tools.Register(new FileReadTool());
        tools.Register(new FileEditTool());
        var toolDefs = tools.GetToolDefinitions();

        var strategies = new (string Name, string? Grammar, bool GrammarMode)[]
        {
            ("A_strict_json", ToolCallGrammarBuilder.BuildStrict(toolDefs), true),
            ("B_tag_wrapped", ToolCallGrammarBuilder.BuildTagWrapped(toolDefs), true),
            ("C_baseline", null, false),
        };

        // Log grammars for debugging
        foreach (var (name, grammar, _) in strategies)
        {
            if (grammar != null)
                await File.WriteAllTextAsync(
                    Path.Combine(ReportDir, $"grammar-{name}.gbnf"), grammar);
        }

        var results = new List<ExperimentResult>();

        foreach (var (stratName, grammar, grammarMode) in strategies)
        {
            // Seed the read test
            await File.WriteAllTextAsync(Path.Combine(TestDir, "hello.txt"), "Hello, World!");

            foreach (var (promptName, promptTemplate, expectedTool) in TestPrompts)
            {
                var prompt = promptTemplate.Replace("{DIR}", TestDir);
                var result = await RunSingleTrial(
                    stratName, grammar, grammarMode, promptName, prompt, expectedTool, tools, toolDefs);
                results.Add(result);

                var status = result.ToolCallParsed ? (result.HasCorrectToolName ? "PASS" : "WRONG") : "FAIL";
                Console.WriteLine(
                    $"[{stratName}] {promptName}: {status} tool={result.ParsedToolName ?? "none"} " +
                    $"time={result.GenerationMs}ms");
            }
        }

        // Write reports
        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var reportPath = Path.Combine(ReportDir, $"grammar-exp-{ts}.json");
        await File.WriteAllTextAsync(reportPath,
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        var summary = BuildSummary(results);
        var summaryPath = Path.Combine(ReportDir, $"grammar-exp-{ts}-summary.txt");
        await File.WriteAllTextAsync(summaryPath, summary);

        Console.WriteLine("\n" + summary);
        Console.WriteLine($"\nReport: {reportPath}");

        Assert.True(results.Any(r => r.ToolCallParsed),
            "No strategy produced a valid tool call for any prompt");
    }

    private async Task<ExperimentResult> RunSingleTrial(
        string strategy, string? grammar, bool grammarMode,
        string promptName, string prompt, string expectedTool,
        CodingToolRegistry tools, IReadOnlyList<ToolDefinition> toolDefs)
    {
        var result = new ExperimentResult
        {
            Strategy = strategy,
            Prompt = promptName,
            Timestamp = DateTime.UtcNow,
        };

        try
        {
            var renderer = new MinionChatRenderer(toolDefs, grammarMode: grammarMode);
            var systemPrompt = $"You are a coding assistant.\n\n# Environment\n- Working directory: {TestDir}";
            using var session = _modelHandle!.CreateChatSession(8192, systemPrompt, renderer);

            var parameters = new GenerationParams
            {
                MaxTokens = 4096,
                Temperature = 0.7f,
                TopK = 40,
                TopP = 0.9f,
                RepetitionPenalty = 1.1f,
                GrammarText = grammar,
            };

            var sw = Stopwatch.StartNew();
            var sb = new StringBuilder();
            await foreach (var token in session.ChatAsync(new ChatMessage("user", prompt), parameters))
                sb.Append(token);
            sw.Stop();

            var response = sb.ToString();
            result.RawResponse = response;
            result.GenerationMs = sw.ElapsedMilliseconds;

            // Parse — grammar strategies may produce raw JSON without <tool_call> tags
            List<ToolCall> calls;
            if (grammarMode && !response.Contains("<tool_call>"))
                calls = TryParseRawJson(response);
            else
                calls = QwenToolCallParser.Parse(response);

            result.ToolCallParsed = calls.Count > 0;
            result.ParsedToolName = calls.Count > 0 ? calls[0].Name : null;
            result.ParsedArgsJson = calls.Count > 0 ? calls[0].Arguments.ToJsonString() : null;
            result.ToolCallCount = calls.Count;

            if (calls.Count > 0)
            {
                result.HasCorrectToolName = calls[0].Name == expectedTool;

                try
                {
                    var execResult = await tools.ExecuteAsync(calls[0], CancellationToken.None);
                    result.ToolExecutionSuccess = !execResult.IsError;
                    result.ToolOutput = execResult.Output;
                }
                catch (Exception ex)
                {
                    result.ToolExecutionSuccess = false;
                    result.ToolOutput = ex.Message;
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Parse raw JSON tool call (no &lt;tool_call&gt; wrapper) as produced by strict grammar.
    /// </summary>
    private static List<ToolCall> TryParseRawJson(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("{")) return [];

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            if (name == null) return [];

            var args = new JsonObject();
            if (root.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => JsonValue.Create(prop.Value.GetString()),
                        JsonValueKind.Number => JsonValue.Create(prop.Value.GetRawText()),
                        JsonValueKind.True => JsonValue.Create(true),
                        JsonValueKind.False => JsonValue.Create(false),
                        _ => JsonNode.Parse(prop.Value.GetRawText()),
                    };
                }
            }

            return [new ToolCall(name, args)];
        }
        catch
        {
            return [];
        }
    }

    private static string BuildSummary(List<ExperimentResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║           Grammar Strategy Experiment Results               ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
        sb.AppendLine();

        var strategies = results.Select(r => r.Strategy).Distinct().ToList();
        var prompts = results.Select(r => r.Prompt).Distinct().ToList();

        sb.Append($"{"Prompt",-16}");
        foreach (var s in strategies)
            sb.Append($"  {s,-18}");
        sb.AppendLine();
        sb.AppendLine(new string('─', 16 + strategies.Count * 20));

        foreach (var p in prompts)
        {
            sb.Append($"{p,-16}");
            foreach (var s in strategies)
            {
                var r = results.FirstOrDefault(x => x.Strategy == s && x.Prompt == p);
                var status = r.ToolCallParsed
                    ? (r.HasCorrectToolName
                        ? (r.ToolExecutionSuccess ? "PASS" : "PARSE-OK")
                        : "WRONG")
                    : "FAIL";
                sb.Append($"  {status,-8} {r.GenerationMs,6}ms ");
            }
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Totals:");
        foreach (var s in strategies)
        {
            var g = results.Where(r => r.Strategy == s).ToList();
            var parsed = g.Count(r => r.ToolCallParsed);
            var correct = g.Count(r => r.HasCorrectToolName);
            var executed = g.Count(r => r.ToolExecutionSuccess);
            var avgMs = g.Average(r => r.GenerationMs);
            sb.AppendLine(
                $"  {s,-18} parsed={parsed}/{g.Count}  correct={correct}/{g.Count}" +
                $"  executed={executed}/{g.Count}  avg={avgMs:F0}ms");
        }

        return sb.ToString();
    }
}

public record ExperimentResult
{
    public string Strategy { get; set; } = "";
    public string Prompt { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? RawResponse { get; set; }
    public long GenerationMs { get; set; }
    public bool ToolCallParsed { get; set; }
    public string? ParsedToolName { get; set; }
    public string? ParsedArgsJson { get; set; }
    public int ToolCallCount { get; set; }
    public bool HasCorrectToolName { get; set; }
    public bool ToolExecutionSuccess { get; set; }
    public string? ToolOutput { get; set; }
    public string? Error { get; set; }
}
