using DaisiGit.SDK;
using Daisi.Minion.Config;

namespace Daisi.Minion.Tests.Modules;

/// <summary>
/// One-time test to inspect and seed the remote modules repo.
/// </summary>
public class SeedModulesTest
{
    [Fact]
    public async Task InspectRemoteRepo()
    {
        var cm = new ConfigManager();
        cm.Load();
        var c = cm.Config;
        if (string.IsNullOrEmpty(c.DaisiGitServer)) Assert.Skip("Not configured");

        using var client = new DaisiGitClient(c.DaisiGitServer, c.DaisiGitToken!);

        var parts = c.ModulesRepo!.Split('/');
        var tree = await client.GetTreeAsync(parts[0], parts[1], c.ModulesBranch);

        // Write to file so we can see it
        var output = $"Repo: {c.ModulesRepo}@{c.ModulesBranch}\nEntries: {tree.Entries.Count}\n";
        foreach (var e in tree.Entries)
            output += $"  {e.Mode} {e.Name} {e.Sha[..7]}\n";

        var outPath = @"C:\minion-dev\repo-tree.txt";
        await File.WriteAllTextAsync(outPath, output);

        // Also try to read any existing module.cs files
        foreach (var e in tree.Entries.Where(x => x.Mode == "040000"))
        {
            try
            {
                var file = await client.GetFileAsync(parts[0], parts[1], $"{e.Name}/module.cs", c.ModulesBranch);
                output += $"\n--- {e.Name}/module.cs ({file.SizeBytes} bytes) ---\n{file.Text?[..Math.Min(file.Text.Length, 200)]}\n";
            }
            catch { output += $"\n--- {e.Name}/module.cs: NOT FOUND ---\n"; }
        }

        await File.WriteAllTextAsync(outPath, output);
        Assert.True(true);
    }
}
