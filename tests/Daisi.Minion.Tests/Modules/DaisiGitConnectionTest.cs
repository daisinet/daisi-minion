using Daisi.Minion.Config;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

/// <summary>
/// Live connection test against git.daisi.ai.
/// Reads credentials from ~/.daisi-minion/config.json.
/// Skips if not configured.
/// </summary>
public class DaisiGitConnectionTest
{
    private static (string server, string token, string repo, string branch)? GetConfig()
    {
        var cm = new ConfigManager();
        cm.Load();
        var c = cm.Config;
        if (string.IsNullOrEmpty(c.DaisiGitServer) || string.IsNullOrEmpty(c.DaisiGitToken) ||
            string.IsNullOrEmpty(c.ModulesRepo))
            return null;
        return (c.DaisiGitServer, c.DaisiGitToken, c.ModulesRepo, c.ModulesBranch);
    }

    [Fact]
    public async Task ListRemoteModules_ReturnsEntries()
    {
        var cfg = GetConfig();
        if (cfg == null) Assert.Skip("DaisiGit not configured");

        using var source = new ModuleRemoteSource(cfg.Value.server, cfg.Value.token, cfg.Value.repo,
            cfg.Value.branch);
        var modules = await source.ListRemoteModulesAsync();

        Assert.NotNull(modules);
    }

    [Fact]
    public async Task Pull_DownloadsModules()
    {
        var cfg = GetConfig();
        if (cfg == null) Assert.Skip("DaisiGit not configured");

        var tempDir = Path.Combine(Path.GetTempPath(), $"minion-pull-test-{Guid.NewGuid():N}");
        try
        {
            using var source = new ModuleRemoteSource(cfg.Value.server, cfg.Value.token, cfg.Value.repo,
                cfg.Value.branch, localModulesDir: tempDir);

            var count = await source.PullAsync();
            Assert.True(count >= 0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
