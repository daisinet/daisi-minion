using Daisi.Minion.Config;
using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

public class ManualPullTest
{
    [Fact]
    public async Task PullToRealModulesDir()
    {
        var cm = new ConfigManager();
        cm.Load();
        var c = cm.Config;

        Assert.True(c.PullModules, "pull_modules should be true");
        Assert.False(string.IsNullOrEmpty(c.ModulesRepo), "modules_repo should be set");
        Assert.False(string.IsNullOrEmpty(c.DaisiGitServer), "server should be set");

        var logs = new List<string>();
        using var source = new ModuleRemoteSource(
            c.DaisiGitServer!, c.DaisiGitToken!, c.ModulesRepo!, c.ModulesBranch,
            log: msg => logs.Add(msg));

        var count = await source.PullAsync();

        // Write logs to a file so we can see them
        var logPath = @"C:\minion-dev\pull-debug.txt";
        await File.WriteAllTextAsync(logPath, string.Join("\n", logs));

        Assert.True(count >= 0, $"Pull returned {count}. Logs:\n{string.Join("\n", logs)}");

        // Verify files exist
        var modulesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");

        Assert.True(Directory.Exists(Path.Combine(modulesDir, "code-reviewer")), "code-reviewer should exist");
        Assert.True(File.Exists(Path.Combine(modulesDir, "code-reviewer", "module.cs")), "code-reviewer/module.cs should exist");
        Assert.True(Directory.Exists(Path.Combine(modulesDir, "safety-guard")), "safety-guard should exist");
        Assert.True(Directory.Exists(Path.Combine(modulesDir, "context-summarizer")), "context-summarizer should exist");
    }
}
