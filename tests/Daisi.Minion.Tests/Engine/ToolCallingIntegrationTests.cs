using System.Text;
using System.Text.Json.Nodes;
using Daisi.Llogos.Chat;
using Daisi.Llogos.Inference;
using Daisi.Inference.Models;
using Daisi.Minion.Coding;
using ChatMessage = Daisi.Llogos.Chat.ChatMessage;
using Daisi.Minion.Coding.Tools;
using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

/// <summary>
/// Integration tests that load a real model, send prompts that should trigger
/// tool calls, parse the response, execute tools, and verify results.
/// Designed to be run iteratively to debug tool-calling issues.
/// </summary>
public class ToolCallingIntegrationTests : IAsyncLifetime
{
    private static readonly string TestModelPath = @"C:\GGUFS\custom\Qwen3.5-9B-Q8_0.gguf";
    private static readonly string TestDir = Path.Combine(Path.GetTempPath(), $"minion-tool-test-{Guid.NewGuid():N}");

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
                ContextSize = 4096,
            });
            _modelHandle = ((DaisiLlogosModelHandleAdapter)adapter).Inner;
            Directory.CreateDirectory(TestDir);
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

    /// <summary>
    /// Send a prompt and collect the full raw response from the model.
    /// </summary>
    private async Task<string> GenerateAsync(DaisiLlogosChatSession session, string userMessage, int maxTokens = 2048)
    {
        var sb = new StringBuilder();
        var parameters = new GenerationParams
        {
            MaxTokens = maxTokens,
            Temperature = 1.0f,
            TopK = 20,
            TopP = 0.95f,
            RepetitionPenalty = 1.0f,
        };

        await foreach (var token in session.ChatAsync(new ChatMessage("user", userMessage), parameters))
        {
            sb.Append(token);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Create a chat session with our custom Qwen tool-calling renderer.
    /// </summary>
    private DaisiLlogosChatSession CreateToolSession()
    {
        // Minimal tool set to keep system prompt small
        var tools = new CodingToolRegistry();
        tools.Register(new FileWriteTool());

        var renderer = new MinionChatRenderer(tools.GetToolDefinitions());
        var systemPrompt = $"You are a coding assistant. Working directory: {TestDir}";

        return _modelHandle!.CreateChatSession(8192, systemPrompt, renderer);
    }

    /// <summary>
    /// Create a chat session using the model's NATIVE chat template (from GGUF).
    /// No custom renderer — uses whatever the model was trained on.
    /// </summary>
    private DaisiLlogosChatSession CreateNativeSession()
    {
        var systemPrompt =
            $"You are a coding assistant with access to tools.\n" +
            $"Working directory: {TestDir}\n\n" +
            "You have a file_write tool. To use it, respond with:\n" +
            "<tool_call>\n{\"name\": \"file_write\", \"arguments\": {\"path\": \"<path>\", \"content\": \"<content>\"}}\n</tool_call>";

        return _modelHandle!.CreateChatSession(4096, systemPrompt);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 1: Model produces <tool_call> in response
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Model_ProducesToolCall_WhenAskedToCreateFile()
    {
        Skip();
        using var session = CreateToolSession();

        var response = await GenerateAsync(session,
            $"Create a file called {Path.Combine(TestDir, "test.html")} containing '<h1>Hello</h1>'. Use the file_write tool.");

        // Log for debugging
        File.WriteAllText(Path.Combine(TestDir, "debug_response.txt"), response);

        Assert.True(QwenToolCallParser.ContainsToolCalls(response),
            $"Expected <tool_call> in response. Got:\n{response[..Math.Min(response.Length, 500)]}");
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 2: Parser extracts tool name and arguments
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Parser_ExtractsToolCall_FromModelOutput()
    {
        Skip();
        using var session = CreateToolSession();

        var response = await GenerateAsync(session,
            $"Create a file at {Path.Combine(TestDir, "parse_test.txt")} with content 'hello world'. Use the file_write tool.");

        var calls = QwenToolCallParser.Parse(response);

        Assert.NotEmpty(calls);
        Assert.Equal("file_write", calls[0].Name);
        Assert.NotNull(calls[0].Arguments["path"]);
        Assert.NotNull(calls[0].Arguments["content"]);

        // Log
        File.WriteAllText(Path.Combine(TestDir, "debug_parsed.txt"),
            $"Name: {calls[0].Name}\nArgs: {calls[0].Arguments.ToJsonString()}\n\nRaw:\n{response}");
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 3: Tool execution creates the file
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ToolExecution_CreatesFile_FromModelOutput()
    {
        Skip();
        using var session = CreateToolSession();

        var targetFile = Path.Combine(TestDir, "created.html");
        var response = await GenerateAsync(session,
            $"Create a file at {targetFile} containing a basic HTML page with <h1>Spacious</h1>. Use file_write.");

        // Debug: persist response even if test fails
        File.WriteAllText(@"C:\minion-dev\debug_tool_test.txt", response);

        var calls = QwenToolCallParser.Parse(response);
        Assert.True(calls.Count > 0,
            $"No tool calls parsed. Response:\n{response[..Math.Min(response.Length, 500)]}");

        // Execute the tool call
        var tool = new FileWriteTool();
        var result = await tool.ExecuteAsync(calls[0].Arguments, CancellationToken.None);

        Assert.False(result.IsError, $"Tool error: {result.Output}");
        Assert.True(File.Exists(targetFile), $"File not created at {targetFile}");

        var content = await File.ReadAllTextAsync(targetFile);
        Assert.Contains("<h1>", content, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 4: Full agentic loop — create file, read it back
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AgenticLoop_CreateAndReadFile()
    {
        Skip();
        var tools = new CodingToolRegistry();
        tools.Register(new FileReadTool());
        tools.Register(new FileWriteTool());
        tools.Register(new FileEditTool());

        var renderer = new MinionChatRenderer(tools.GetToolDefinitions());
        var systemPrompt = $"You are a coding assistant.\n\n# Environment\n- Working directory: {TestDir}";
        using var session = _modelHandle!.CreateChatSession(4096, systemPrompt, renderer);

        var targetFile = Path.Combine(TestDir, "loop_test.html");

        // Step 1: Ask to create
        var response = await GenerateAsync(session,
            $"Create {targetFile} with a Bootstrap page titled 'Spacious'. Use file_write.");

        var calls = QwenToolCallParser.Parse(response);
        Assert.NotEmpty(calls);

        // Execute
        var result = await tools.ExecuteAsync(calls[0], CancellationToken.None);
        Assert.False(result.IsError, $"Write failed: {result.Output}");
        Assert.True(File.Exists(targetFile));

        // Inject tool result and ask to read it back
        var toolMsg = MinionToolFormatter.Instance.FormatToolResult(calls[0].Name, result.Output);
        session.AddMessage(toolMsg);

        var response2 = await GenerateAsync(session,
            $"Now read {targetFile} to verify it was created correctly. Use file_read.");

        var calls2 = QwenToolCallParser.Parse(response2);

        // The model should try to read the file
        Assert.NotEmpty(calls2);
        Assert.Equal("file_read", calls2[0].Name);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 5: Build a complete website (the real goal)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task BuildWebsite_CreatesIndexHtml()
    {
        Skip();
        using var session = CreateToolSession();

        var targetFile = Path.Combine(TestDir, "index.html");
        var response = await GenerateAsync(session,
            $"Create {targetFile} — a Bootstrap 5 website for 'Spacious' coworking. " +
            "Black and white colors. Include a navbar with Home, Private Offices, Events links. " +
            "Include a hero section and contact form. Use file_write with the complete HTML.",
            maxTokens: 4096);

        // Log the full response for debugging
        File.WriteAllText(Path.Combine(TestDir, "debug_website.txt"), response);

        var calls = QwenToolCallParser.Parse(response);
        Assert.NotEmpty(calls);

        var writeCalls = calls.Where(c => c.Name == "file_write").ToList();
        Assert.NotEmpty(writeCalls);

        // Execute all write calls
        var tool = new FileWriteTool();
        foreach (var call in writeCalls)
        {
            var result = await tool.ExecuteAsync(call.Arguments, CancellationToken.None);
            Assert.False(result.IsError, $"Write failed: {result.Output}");
        }

        Assert.True(File.Exists(targetFile), "index.html not created");

        var content = await File.ReadAllTextAsync(targetFile);
        Assert.Contains("Spacious", content);
        Assert.Contains("bootstrap", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<nav", content, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 8: Try native template with JSON tool calls
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task NativeTemplate_ProducesToolCall()
    {
        Skip();
        using var session = CreateNativeSession();

        var targetFile = Path.Combine(TestDir, "native_test.html");
        var response = await GenerateAsync(session,
            $"Create a file at {targetFile} containing '<h1>Hello</h1>'. Respond ONLY with a tool_call, nothing else.");

        // Write debug output
        var debugPath = Path.Combine(TestDir, "debug_native.txt");
        Directory.CreateDirectory(TestDir);
        File.WriteAllText(debugPath, response);

        // Check for either XML or JSON format tool calls
        var hasToolCall = response.Contains("<tool_call>") ||
                          response.Contains("\"name\"") && response.Contains("file_write");

        Assert.True(hasToolCall,
            $"Expected tool call in response. Got:\n{response[..Math.Min(response.Length, 500)]}");
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 6: Verify the parser handles missing </tool_call>
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Parser_HandlesMissingCloseTag()
    {
        var text = @"<tool_call>
<function=file_write>
<parameter=path>
test.html
</parameter>
<parameter=content>
<h1>Hello</h1>
</parameter>
</function>
";
        // No </tool_call> — consumed by stop sequence

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_write", calls[0].Name);
        Assert.Equal("test.html", calls[0].Arguments["path"]!.GetValue<string>());
        Assert.Contains("<h1>Hello</h1>", calls[0].Arguments["content"]!.GetValue<string>());
    }

    // ══════════════════════════════════════════════════════════════
    //  TEST 7: Verify parser handles content with HTML inside
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Parser_HandlesHtmlInContent()
    {
        var text = @"<tool_call>
<function=file_write>
<parameter=path>
index.html
</parameter>
<parameter=content>
<!DOCTYPE html>
<html>
<head><title>Test</title></head>
<body>
<nav class=""navbar"">
  <a href=""/"">Home</a>
</nav>
<h1>Welcome</h1>
</body>
</html>
</parameter>
</function>
";

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_write", calls[0].Name);
        var content = calls[0].Arguments["content"]!.GetValue<string>();
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("<nav", content);
        Assert.Contains("</html>", content);
    }
}
