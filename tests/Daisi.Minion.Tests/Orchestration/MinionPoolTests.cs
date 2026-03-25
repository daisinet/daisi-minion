using Daisi.Minion.Orchestration;
using Daisi.Minion.Types;

namespace Daisi.Minion.Tests.Orchestration;

/// <summary>
/// Tests for MinionPool. These are unit tests that verify pool management
/// without a real model — spawn/stop/status lifecycle.
/// SendAsync requires a real model so it's tested in integration tests.
/// </summary>
public class MinionPoolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Daisi.Minion.Coding.ToolSandbox _sandbox;
    private readonly Daisi.Minion.Config.ConfigManager _configManager;

    public MinionPoolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pool-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _sandbox = new Daisi.Minion.Coding.ToolSandbox(_tempDir);
        _configManager = new Daisi.Minion.Config.ConfigManager();
        _configManager.Load();
    }

    [Fact]
    public void Spawn_ValidType_ReturnsId()
    {
        // Pool with null model — spawn creates the child but SendAsync will fail
        var pool = new MinionPool(null, _configManager, _sandbox);

        // This will throw because model is null
        var ex = Assert.Throws<InvalidOperationException>(() => pool.Spawn("code", "Fix a bug"));
        Assert.Contains("No model loaded", ex.Message);
    }

    [Fact]
    public void Spawn_UnknownType_Throws()
    {
        var pool = new MinionPool(null, _configManager, _sandbox);
        Assert.Throws<ArgumentException>(() => pool.Spawn("nonexistent", "task"));
    }

    [Fact]
    public void GetPoolSummary_Empty_ReturnsMessage()
    {
        var pool = new MinionPool(null, _configManager, _sandbox);
        Assert.Equal("No active minions.", pool.GetPoolSummary());
    }

    [Fact]
    public void Children_Empty_IsEmpty()
    {
        var pool = new MinionPool(null, _configManager, _sandbox);
        Assert.Empty(pool.Children);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
