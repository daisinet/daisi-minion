using System.Text;
using Daisi.Llogos.Chat;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Config;
using Daisi.Protos.V1;
using Daisi.SDK.Clients.V1.Host;
using Daisi.SDK.Clients.V1.Orc;
using Daisi.SDK.Models;

namespace Daisi.Minion.Engine;

/// <summary>
/// Parallel goal runner that routes inference through daisinet's ORC instead of a local GGUF.
/// Owns its InferenceClient lifecycle, SECRET→CLIENT exchange, model resolution, and a goal
/// loop that reuses the existing tool registry + MinionToolFormatter for local tool execution.
///
/// Used only when `--backend daisinet --goal X`. Does not subclass MinionBase — the model handle,
/// chat harness, and conversation manager are intentionally bypassed because they're Llogos-specific.
/// </summary>
public sealed class DaisinetGoalRunner : IDisposable
{
    private readonly string _goal;
    private readonly int _maxIterations;
    private readonly string? _explicitModel;
    private readonly string? _roleName;
    private readonly bool _jsonOutput;
    private readonly string _workDir;

    private readonly ToolSandbox _sandbox;
    private readonly CodingToolRegistry _toolRegistry = new();
    private readonly RoleManager _roles = new();
    private readonly ProjectContext _projectContext;
    private InferenceClient? _inferenceClient;

    public DaisinetGoalRunner(Options options)
    {
        _goal = options.Goal;
        _maxIterations = options.MaxIterations > 0 ? options.MaxIterations : 20;
        _explicitModel = string.IsNullOrWhiteSpace(options.Model) ? null : options.Model;
        _roleName = string.IsNullOrWhiteSpace(options.Role) ? null : options.Role;
        _jsonOutput = options.JsonOutput;
        _workDir = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : options.WorkingDirectory;

        _sandbox = new ToolSandbox(_workDir);
        _projectContext = new ProjectContext(_workDir);
        RegisterBaseTools();
    }

    public sealed class Options
    {
        public string Goal { get; init; } = "";
        public int MaxIterations { get; init; } = 20;
        public string? Model { get; init; }
        public string? Role { get; init; }
        public bool JsonOutput { get; init; }
        public string? WorkingDirectory { get; init; }
    }

    private void RegisterBaseTools()
    {
        _toolRegistry.Register(new FileReadTool(_sandbox));
        _toolRegistry.Register(new FileWriteTool(_sandbox));
        _toolRegistry.Register(new FileEditTool(_sandbox));
        _toolRegistry.Register(new GrepTool(_sandbox));
        _toolRegistry.Register(new GlobTool(_sandbox));
        _toolRegistry.Register(new ShellExecuteTool(_sandbox));
        _toolRegistry.Register(new GitTool(_sandbox));
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        // 1. Apply env-var config to static SDK settings.
        ApplyEnvConfig();

        if (string.IsNullOrEmpty(DaisiStaticSettings.SecretKey))
        {
            WriteError("DAISI_SECRET_KEY is not set. Daisinet backend requires a SECRET-KEY to exchange for a short-lived client key.");
            return 1;
        }

        // 2. SECRET → CLIENT exchange. Throws on bad SECRET-KEY.
        try
        {
            new AuthClientFactory().CreateStaticClientKey();
        }
        catch (Exception ex)
        {
            WriteError($"Failed to acquire client key from ORC: {ex.Message}");
            return 1;
        }

        // 3. Resolve model name.
        string modelName;
        try
        {
            modelName = _explicitModel ?? await ResolveDefaultModelAsync();
        }
        catch (Exception ex)
        {
            WriteError($"Could not resolve daisinet model: {ex.Message}");
            return 1;
        }
        WriteInfo($"Model: {modelName}");

        // 4. Refresh project context for the system prompt.
        await _projectContext.RefreshAsync(ct);
        var systemPrompt = BuildSystemPrompt();

        // 5. Create inference session.
        try
        {
            _inferenceClient = new InferenceClientFactory().Create();
            _inferenceClient.Create(new CreateInferenceRequest
            {
                ModelName = modelName,
                InitializationPrompt = systemPrompt,
                ThinkLevel = ThinkLevels.Basic,
                SkipIdentityPreamble = true,
            });
        }
        catch (Exception ex)
        {
            WriteError($"Failed to open inference session: {ex.Message}");
            return 1;
        }

        // 6. Goal loop.
        var userMessage =
            $"Your goal: {_goal}\n\n" +
            "Call tools immediately to make progress. Do not explain what you plan to do — just call the tool.\n" +
            "When the goal is complete, emit a tool call: {\"name\":\"complete\",\"arguments\":{\"summary\":\"<brief summary>\"}}";
        var formatter = MinionToolFormatter.Instance;

        for (int iteration = 1; iteration <= _maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            WriteInfo($"--- Iteration {iteration}/{_maxIterations} ---");

            string responseText;
            try
            {
                responseText = await StreamResponseAsync(userMessage, ct);
            }
            catch (Exception ex)
            {
                WriteError($"Inference error: {ex.Message}");
                return 1;
            }

            if (!formatter.ContainsToolCalls(responseText))
            {
                // No tool calls — the model answered with prose. Treat as goal-complete for this run.
                WriteInfo("No tool calls emitted; treating as pure-text completion.");
                return 0;
            }

            var toolCalls = formatter.ParseToolCalls(responseText);
            if (toolCalls.Count == 0)
            {
                WriteInfo("Tool call malformed — asking model to retry.");
                userMessage = "Your tool call had invalid JSON. Use this exact format: {\"name\":\"tool_name\",\"arguments\":{\"key\":\"value\"}}.";
                continue;
            }

            // Handle the "complete" sentinel before executing.
            var completeCall = toolCalls.FirstOrDefault(c =>
                string.Equals(c.Name, "complete", StringComparison.OrdinalIgnoreCase));
            if (completeCall != null)
            {
                var summary = completeCall.Arguments["summary"]?.ToString() ?? "done";
                WriteInfo($"GOAL_COMPLETE: {summary}");
                return 0;
            }

            // Execute each tool locally; build a combined result message for the next turn.
            var resultsMessage = new StringBuilder();
            foreach (var call in toolCalls)
            {
                WriteInfo($"  [{call.Name}] {Truncate(call.Arguments.ToJsonString(), 120)}");
                var result = await _toolRegistry.ExecuteAsync(call, ct);
                WriteInfo(result.IsError ? $"  ✗ {Truncate(result.Output, 200)}" : $"  ✓ {Truncate(result.Output, 200)}");

                resultsMessage.AppendLine($"<tool_result name=\"{call.Name}\">");
                resultsMessage.AppendLine(result.Output);
                resultsMessage.AppendLine("</tool_result>");
            }
            userMessage = resultsMessage.ToString();
        }

        WriteInfo($"Reached max iterations ({_maxIterations}). Goal may not be fully complete.");
        return 2;
    }

    private async Task<string> StreamResponseAsync(string userMessage, CancellationToken ct)
    {
        var accumulated = new StringBuilder();
        using var call = _inferenceClient!.Send(userMessage, ThinkLevels.Basic);
        while (await call.ResponseStream.MoveNext(ct))
        {
            var chunk = call.ResponseStream.Current;
            switch (chunk.Type)
            {
                case InferenceResponseTypes.Text:
                    accumulated.Append(chunk.Content);
                    Console.Out.Write(chunk.Content);
                    break;
                case InferenceResponseTypes.Thinking:
                    if (_jsonOutput)
                        Console.Error.WriteLine($"{{\"event\":\"thinking\",\"content\":\"{Escape(Truncate(chunk.Content, 500))}\"}}");
                    break;
                case InferenceResponseTypes.Tooling:
                    // ORC-side tool event. We ignore — our loop parses tool calls from Text content.
                    break;
                case InferenceResponseTypes.Error:
                    throw new InvalidOperationException($"ORC error: {chunk.Content}");
            }
        }
        Console.Out.WriteLine();
        return accumulated.ToString();
    }

    private static async Task<string> ResolveDefaultModelAsync()
    {
        var modelClient = new ModelClientFactory().Create();
        var response = await modelClient.GetRequiredModelsAsync(new GetRequiredModelsRequest()).ResponseAsync;

        var textGenEnabled = response.Models
            .Where(m => m.Enabled && m.Type == AIModelTypes.TextGeneration)
            .ToList();

        var pick = textGenEnabled.FirstOrDefault(m => m.IsDefault)
                   ?? textGenEnabled.FirstOrDefault();

        if (pick == null)
            throw new InvalidOperationException("No enabled text-generation model available on ORC.");

        return pick.Name;
    }

    private static void ApplyEnvConfig()
    {
        var address = Environment.GetEnvironmentVariable("DAISI_ORC_ADDRESS");
        if (!string.IsNullOrWhiteSpace(address))
        {
            var parts = address.Split(':', 2);
            DaisiStaticSettings.OrcIpAddressOrDomain = parts[0];
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                DaisiStaticSettings.OrcPort = port;
                DaisiStaticSettings.OrcUseSSL = port == 443;
            }
        }
        var useSsl = Environment.GetEnvironmentVariable("DAISI_ORC_USE_SSL");
        if (bool.TryParse(useSsl, out var ssl)) DaisiStaticSettings.OrcUseSSL = ssl;

        var secretKey = Environment.GetEnvironmentVariable("DAISI_SECRET_KEY");
        if (!string.IsNullOrWhiteSpace(secretKey))
            DaisiStaticSettings.SecretKey = secretKey;
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are daisi-minion, an autonomous coding agent operating inside a workflow runner.");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(_roleName))
        {
            var roleContent = _roles.GetContent(_roleName);
            if (roleContent != null)
            {
                sb.AppendLine(roleContent);
                sb.AppendLine();
            }
        }

        sb.Append(_projectContext.ToSystemPromptSection());
        sb.AppendLine();

        sb.AppendLine(ToolPromptFormatter.FormatToolsBlock(_toolRegistry.GetToolDefinitions()));
        sb.AppendLine();
        sb.AppendLine("When you need to use a tool, emit one JSON object per call: {\"name\":\"tool_name\",\"arguments\":{...}}");
        sb.AppendLine("When the goal is complete, emit: {\"name\":\"complete\",\"arguments\":{\"summary\":\"<brief summary>\"}}");

        return sb.ToString();
    }

    private void WriteInfo(string message)
    {
        if (_jsonOutput)
            Console.Error.WriteLine($"{{\"event\":\"info\",\"message\":\"{Escape(message)}\"}}");
        else
            Console.Error.WriteLine(message);
    }

    private void WriteError(string message) =>
        Console.Error.WriteLine(_jsonOutput
            ? $"{{\"event\":\"error\",\"message\":\"{Escape(message)}\"}}"
            : $"Error: {message}");

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s.Replace('\n', ' ') : s[..max].Replace('\n', ' ') + "...";

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    public void Dispose()
    {
        try { _inferenceClient?.CloseAsync(closeOrcSession: true).GetAwaiter().GetResult(); }
        catch { }
    }
}
