using System.Text.Json.Nodes;
using Daisi.Minion.Orchestration;

namespace Daisi.Minion.Tests.Orchestration;

public class SummonerToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Daisi.Minion.Coding.ToolSandbox _sandbox;
    private readonly Daisi.Minion.Config.ConfigManager _configManager;
    private readonly MinionPool _pool;

    public SummonerToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "summoner-tools-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _sandbox = new Daisi.Minion.Coding.ToolSandbox(_tempDir);
        _configManager = new Daisi.Minion.Config.ConfigManager();
        _configManager.Load();
        _pool = new MinionPool(null, _configManager, _sandbox);
    }

    [Fact]
    public async Task SpawnMinionTool_NoModel_ReturnsError()
    {
        var tool = new SpawnMinionTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["minion_type"] = "code",
            ["task"] = "Fix a bug",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("No model loaded", result.Output);
    }

    [Fact]
    public async Task SpawnMinionTool_MissingType_ReturnsError()
    {
        var tool = new SpawnMinionTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["task"] = "Fix a bug",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("minion_type", result.Output);
    }

    [Fact]
    public async Task SpawnMinionTool_MissingTask_ReturnsError()
    {
        var tool = new SpawnMinionTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["minion_type"] = "code",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("task", result.Output);
    }

    [Fact]
    public async Task CheckMinionTool_UnknownId_ReturnsError()
    {
        var tool = new CheckMinionTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["minion_id"] = "nonexistent-1",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Unknown minion", result.Output);
    }

    [Fact]
    public async Task StopMinionTool_UnknownId_Succeeds()
    {
        // Stop on unknown ID doesn't error — it's idempotent
        var tool = new StopMinionTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["minion_id"] = "nonexistent-1",
        }, CancellationToken.None);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ListMinionsTool_EmptyPool_ReturnsMessage()
    {
        var tool = new ListMinionsTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("No active minions", result.Output);
    }

    [Fact]
    public async Task SendMessageTool_UnknownId_ReturnsError()
    {
        var tool = new SendMessageTool(_pool);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["minion_id"] = "nonexistent-1",
            ["message"] = "Hello",
        }, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public void SpawnMinionTool_HasCorrectSchema()
    {
        var tool = new SpawnMinionTool(_pool);
        Assert.Equal("spawn_minion", tool.Name);
        Assert.NotNull(tool.ParametersSchema["properties"]?["minion_type"]);
        Assert.NotNull(tool.ParametersSchema["properties"]?["task"]);
    }

    [Fact]
    public void AllSummonerTools_HaveDistinctNames()
    {
        var tools = new Daisi.Minion.Coding.IMinionTool[]
        {
            new SpawnMinionTool(_pool),
            new CheckMinionTool(_pool),
            new SendMessageTool(_pool),
            new StopMinionTool(_pool),
            new ListMinionsTool(_pool),
        };

        var names = tools.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void SummonerTypeConfig_Exists()
    {
        Assert.True(Daisi.Minion.Types.MinionTypeFactory.Exists("summoner"));
        var config = Daisi.Minion.Types.MinionTypeFactory.Get("summoner");
        Assert.NotNull(config.SystemPromptExtension);
        Assert.Contains("Summoner", config.SystemPromptExtension);
        Assert.NotNull(config.AllowedTools);
        Assert.Empty(config.AllowedTools); // no base file tools
    }

    public void Dispose()
    {
        _pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
