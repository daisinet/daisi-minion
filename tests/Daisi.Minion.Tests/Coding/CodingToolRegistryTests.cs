using System.Text.Json.Nodes;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Tests.Coding;

public class CodingToolRegistryTests
{
    [Fact]
    public void RegistersAndListsTools()
    {
        var registry = new CodingToolRegistry();
        registry.Register(new FileReadTool());
        registry.Register(new FileWriteTool());

        Assert.Equal(2, registry.Tools.Count);
        Assert.Contains("file_read", registry.Tools.Keys);
        Assert.Contains("file_write", registry.Tools.Keys);
    }

    [Fact]
    public void GetToolDefinitions_ReturnsAllTools()
    {
        var registry = new CodingToolRegistry();
        registry.Register(new FileReadTool());

        var defs = registry.GetToolDefinitions();

        Assert.Single(defs);
        Assert.Equal("file_read", defs[0].Name);
        Assert.NotEmpty(defs[0].Description);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var registry = new CodingToolRegistry();
        var call = new ToolCall("nonexistent", new JsonObject());

        var result = await registry.ExecuteAsync(call, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Unknown tool", result.Output);
    }

    [Fact]
    public void SealBaseTools_PreventsReplacement()
    {
        var registry = new CodingToolRegistry();
        registry.Register(new FileReadTool());
        registry.SealBaseTools();

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new FileReadTool()));
    }

    [Fact]
    public void SealBaseTools_AllowsNewTools()
    {
        var registry = new CodingToolRegistry();
        registry.Register(new FileReadTool());
        registry.SealBaseTools();

        // A tool with a different name should work fine
        registry.Register(new FileWriteTool());
        Assert.Equal(2, registry.Tools.Count);
    }

    [Fact]
    public void SealBaseTools_EmptyRegistry_NoError()
    {
        var registry = new CodingToolRegistry();
        registry.SealBaseTools(); // should not throw
        registry.Register(new FileReadTool());
        Assert.Single(registry.Tools);
    }
}
