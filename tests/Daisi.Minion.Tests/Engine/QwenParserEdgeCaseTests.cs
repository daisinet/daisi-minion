using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

public class QwenParserEdgeCaseTests
{
    [Fact]
    public void Parse_JsonWithThinkTagBefore_Works()
    {
        var text = """
            </think>

            <tool_call>
            {"name": "file_edit", "arguments": {"path": "about.html", "old_string": "WORK", "new_string": "ABOUT"}}
            """;

        Assert.True(QwenToolCallParser.ContainsToolCalls(text));
        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_edit", calls[0].Name);
        Assert.Equal("about.html", calls[0].Arguments["path"]!.GetValue<string>());
    }

    [Fact]
    public void Parse_JsonWithMissingCloseTag_Works()
    {
        var text = """
            <tool_call>
            {"name": "file_read", "arguments": {"path": "test.txt"}}
            """;

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_read", calls[0].Name);
    }

    [Fact]
    public void Parse_JsonWithMalformedKeyQuotes_Works()
    {
        // Model sometimes produces: arguments": instead of "arguments":
        var text = """
            <tool_call>
            {"name": "file_edit", arguments": {"path": "a.html", "old_string": "X", "new_string": "Y"}}
            </tool_call>
            """;

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_edit", calls[0].Name);
    }

    [Fact]
    public void Parse_ValidJsonToolCall_Works()
    {
        var text = """
            <tool_call>
            {"name": "shell", "arguments": {"command": "dir"}}
            </tool_call>
            """;

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("shell", calls[0].Name);
        Assert.Equal("dir", calls[0].Arguments["command"]!.GetValue<string>());
    }

    [Fact]
    public void Parse_JsonWithNewlinesInValue_Works()
    {
        var text = """
            <tool_call>
            {"name": "file_write", "arguments": {"path": "test.py", "content": "print('hello')\nprint('world')"}}
            </tool_call>
            """;

        var calls = QwenToolCallParser.Parse(text);
        Assert.Single(calls);
        Assert.Equal("file_write", calls[0].Name);
    }

    [Fact]
    public void ContainsToolCalls_WithThinkTagPrefix_ReturnsTrue()
    {
        var text = "</think>\n\n<tool_call>\n{\"name\": \"test\"}\n";
        Assert.True(QwenToolCallParser.ContainsToolCalls(text));
    }

    [Fact]
    public void ContainsToolCalls_NoToolCall_ReturnsFalse()
    {
        Assert.False(QwenToolCallParser.ContainsToolCalls("Just regular text."));
    }
}
