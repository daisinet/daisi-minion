using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

public class ModuleRemoteSourceTests
{
    [Fact]
    public void Constructor_InvalidRepo_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ModuleRemoteSource("https://git.daisi.ai", "dg_test", "no-slash"));
    }

    [Fact]
    public void Constructor_ValidRepo_Parses()
    {
        using var source = new ModuleRemoteSource(
            "https://git.daisi.ai", "dg_test", "myuser/my-modules");
        // Should not throw — owner/slug parsed correctly
    }

    [Fact]
    public async Task Pull_InvalidServer_LogsErrorAndReturnsZero()
    {
        var logs = new List<string>();
        using var source = new ModuleRemoteSource(
            "https://invalid.example.com", "dg_fake", "user/repo",
            log: msg => logs.Add(msg));

        var count = await source.PullAsync();

        Assert.Equal(0, count);
        Assert.Contains(logs, l => l.Contains("Failed to browse repo"));
    }

    [Fact]
    public async Task ListRemoteModules_InvalidServer_Throws()
    {
        using var source = new ModuleRemoteSource(
            "https://invalid.example.com", "dg_fake", "user/repo");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => source.ListRemoteModulesAsync());
    }

    [Fact]
    public async Task PushModule_MissingModule_Throws()
    {
        using var source = new ModuleRemoteSource(
            "https://git.daisi.ai", "dg_test", "user/repo",
            localModulesDir: Path.Combine(Path.GetTempPath(), $"minion-test-{Guid.NewGuid():N}"));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => source.PushModuleAsync("nonexistent"));
    }
}
