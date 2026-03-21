using System.Text.Json.Nodes;
using Daisi.Llogos.Chat;
using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

public class MinionChatRendererTests
{
    private static readonly List<ToolDefinition> TestTools =
    [
        new("file_read", "Read a file.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path" }
            },
            ["required"] = new JsonArray("path")
        }),
        new("shell", "Run a shell command.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = new JsonObject { ["type"] = "string", ["description"] = "Command to run" }
            },
            ["required"] = new JsonArray("command")
        }),
    ];

    private static MinionChatRenderer CreateRenderer() => new(TestTools);
    private static MinionChatRenderer CreateRendererNoTools() => new(Array.Empty<ToolDefinition>());

    // ── System prompt with tools ──

    [Fact]
    public void Render_InjectsToolsBlock_InSystemMessage()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "You are a coding assistant."),
            new("user", "Hello"),
        };

        var result = renderer.Render(messages);

        Assert.Contains("<tools>", result);
        Assert.Contains("</tools>", result);
        Assert.Contains("file_read", result);
        Assert.Contains("shell", result);
        Assert.Contains("tool_call", result); // instructions for tool call format
    }

    [Fact]
    public void Render_SystemContent_BeforeToolsBlock()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "You are a coding assistant."),
            new("user", "Hi"),
        };

        var result = renderer.Render(messages);

        var systemContentIdx = result.IndexOf("You are a coding assistant.");
        var toolsIdx = result.IndexOf("<tools>");
        Assert.True(systemContentIdx < toolsIdx,
            "System content should appear before tools block");
    }

    [Fact]
    public void Render_NoTools_RenderSystemNormally()
    {
        var renderer = CreateRendererNoTools();
        var messages = new List<ChatMessage>
        {
            new("system", "You are helpful."),
            new("user", "Hi"),
        };

        var result = renderer.Render(messages);

        Assert.DoesNotContain("<tools>", result);
        Assert.Contains("<|im_start|>system\nYou are helpful.<|im_end|>", result);
    }

    // ── Tool response grouping ──

    [Fact]
    public void Render_ToolResponses_WrappedInToolResponseTags()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Read a.txt"),
            new("assistant", "<tool_call>\n{\"name\":\"file_read\",\"arguments\":{\"path\":\"a.txt\"}}\n</tool_call>"),
            new("tool", "contents of a.txt"),
        };

        var result = renderer.Render(messages);

        Assert.Contains("<tool_response>", result);
        Assert.Contains("contents of a.txt", result);
        Assert.Contains("</tool_response>", result);
    }

    [Fact]
    public void Render_ConsecutiveToolResponses_GroupedInSingleUserTurn()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Read both files"),
            new("assistant", "I'll read both."),
            new("tool", "content of a.txt"),
            new("tool", "content of b.txt"),
        };

        var result = renderer.Render(messages);

        // Two tool responses should be grouped in a single <|im_start|>user turn
        var userStarts = CountOccurrences(result, "<|im_start|>user");
        // One for the real user message "Read both files", one for the grouped tool responses
        Assert.Equal(2, userStarts);

        // Both responses should be present
        Assert.Contains("content of a.txt", result);
        Assert.Contains("content of b.txt", result);
    }

    // ── Thinking support ──

    [Fact]
    public void Render_GenerationPrompt_IncludesThinkTag()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Hello"),
        };

        var result = renderer.Render(messages, addGenerationPrompt: true);

        Assert.EndsWith("<|im_start|>assistant\n<think>\n", result);
    }

    [Fact]
    public void Render_NoGenerationPrompt_OmitsThinkTag()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Hello"),
        };

        var result = renderer.Render(messages, addGenerationPrompt: false);

        Assert.DoesNotContain("<think>", result.Split("Hello")[1]);
    }

    [Fact]
    public void Render_AssistantWithThinking_PreservesThinkContent()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "What is 2+2?"),
            new("assistant", "<think>\nLet me calculate...\n</think>\n\n4"),
        };

        var result = renderer.Render(messages, addGenerationPrompt: false);

        // The thinking content should be rendered in the output
        Assert.Contains("Let me calculate", result);
        Assert.Contains("4", result);
    }

    // ── Stop sequences ──

    [Fact]
    public void StopSequences_IncludesChatMLEnd()
    {
        var renderer = CreateRenderer();
        var stops = renderer.GetStopSequences();

        Assert.Contains("<|im_end|>", stops);
    }

    [Fact]
    public void StopSequences_IncludesToolCallEnd()
    {
        var renderer = CreateRenderer();
        var stops = renderer.GetStopSequences();

        Assert.Contains("</tool_call>", stops);
    }

    // ── Multi-step tool interaction ──

    [Fact]
    public void Render_MultiStepToolLoop_CorrectMessageOrder()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Fix the bug in auth.cs"),
            // Step 1: model reads file
            new("assistant", "<tool_call>\n{\"name\":\"file_read\",\"arguments\":{\"path\":\"auth.cs\"}}\n</tool_call>"),
            new("tool", "class Auth { /* broken */ }"),
            // Step 2: model writes fix
            new("assistant", "<think>\nI see the bug.\n</think>\n\n<tool_call>\n{\"name\":\"file_write\",\"arguments\":{\"path\":\"auth.cs\",\"content\":\"class Auth { /* fixed */ }\"}}\n</tool_call>"),
            new("tool", "Created auth.cs (1 lines)"),
            // Step 3: model confirms
            new("assistant", "I've fixed the bug in auth.cs."),
        };

        var result = renderer.Render(messages, addGenerationPrompt: false);

        // Verify ordering: user query appears before tool calls, which appear before final response
        var queryIdx = result.IndexOf("Fix the bug in auth.cs");
        var readIdx = result.IndexOf("class Auth { /* broken */ }");
        var fixedIdx = result.IndexOf("I've fixed the bug");

        Assert.True(queryIdx < readIdx, "User query before tool result");
        Assert.True(readIdx < fixedIdx, "Tool result before final response");
    }

    [Fact]
    public void Render_ToolDefinitions_ContainJsonSchema()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("system", "System"),
            new("user", "Hi"),
        };

        var result = renderer.Render(messages);

        // Tool definitions should include JSON schema info
        Assert.Contains("\"type\":\"function\"", result);
        Assert.Contains("\"name\":\"file_read\"", result);
        Assert.Contains("\"description\":\"Read a file.\"", result);
        Assert.Contains("\"parameters\":", result);
    }

    // ── Edge cases ──

    [Fact]
    public void Render_NoSystemMessage_ToolsStillInjected()
    {
        var renderer = CreateRenderer();
        var messages = new List<ChatMessage>
        {
            new("user", "Hello"),
        };

        var result = renderer.Render(messages);

        // Tools block should still be in the system turn even without user-provided system content
        Assert.Contains("<|im_start|>system", result);
        Assert.Contains("<tools>", result);
    }

    [Fact]
    public void Render_EmptyMessages_GenerationPromptOnly()
    {
        var renderer = CreateRenderer();
        var result = renderer.Render([], addGenerationPrompt: true);

        // Should have tools system block + generation prompt
        Assert.Contains("<|im_start|>system", result);
        Assert.Contains("<|im_start|>assistant\n<think>\n", result);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
