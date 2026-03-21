using Daisi.Minion.Config;
using Daisi.Minion.Engine;

namespace Daisi.Minion.Tests.Engine;

public class CliRunnerTests : IDisposable
{
    private readonly ConfigManager _configManager = new();

    public CliRunnerTests()
    {
        _configManager.Load();
    }

    public void Dispose()
    {
        // nothing to clean up
    }

    [Fact]
    public void ParseArgs_Goal()
    {
        using var runner = new CliRunner(_configManager);
        runner.ParseArgs(["--cli", "--goal", "Fix the bug"]);
        // No exception means args parsed successfully.
        // Goal is private, so we verify it runs goal mode via RunAsync (integration test).
    }

    [Fact]
    public void ParseArgs_AllFlags()
    {
        using var runner = new CliRunner(_configManager);
        runner.ParseArgs([
            "--cli",
            "--goal", "test goal",
            "--model", "/fake/model.gguf",
            "--context", "4096",
            "--backend", "cpu",
            "--max-tokens", "2048",
            "--max-iterations", "5",
            "--role", "coder",
            "--json"
        ]);
        // All flags parsed without exception
    }

    [Fact]
    public void ParseArgs_InvalidNumbersIgnored()
    {
        using var runner = new CliRunner(_configManager);
        // Non-numeric values for numeric flags should be silently ignored
        runner.ParseArgs(["--cli", "--context", "abc", "--max-tokens", "xyz"]);
    }

    [Fact]
    public void ParseArgs_Empty()
    {
        using var runner = new CliRunner(_configManager);
        runner.ParseArgs([]);
    }

    [Fact]
    public async Task RunAsync_NoModel_ReturnsExitCode1()
    {
        var config = new ConfigManager();
        config.Load();
        // Point to a nonexistent models directory so no model is found
        config.Config.ActiveModel = null;
        config.Config.ModelsDirectory = Path.Combine(Path.GetTempPath(), $"no-models-{Guid.NewGuid():N}");

        using var runner = new CliRunner(config);
        runner.ParseArgs(["--cli", "--model", "/nonexistent/model.gguf"]);

        var exitCode = await runner.RunAsync();

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var runner = new CliRunner(_configManager);
        runner.Dispose();
        runner.Dispose(); // second dispose should not throw
    }
}
