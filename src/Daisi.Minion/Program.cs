using Daisi.Minion.Config;
using Daisi.Minion.Engine;
using Daisi.Minion.Types;

// Load configuration
var configManager = new ConfigManager();
configManager.Load();

// Parse --type flag (applies to both CLI and TUI modes)
MinionTypeConfig? typeConfig = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--type" && i + 1 < args.Length)
    {
        typeConfig = MinionTypeFactory.Get(args[i + 1]);
        break;
    }
}

// CLI mode: headless, no TUI, plain text to stdout/stderr
if (args.Contains("--cli"))
{
    // Daisinet backend takes a dedicated path — it bypasses local-GGUF machinery entirely.
    // Requires --goal; interactive mode with daisinet is not supported in this runner.
    if (HasFlagValue(args, "--backend", out var backend) &&
        string.Equals(backend, "daisinet", StringComparison.OrdinalIgnoreCase))
    {
        if (!HasFlagValue(args, "--goal", out var goal) || string.IsNullOrWhiteSpace(goal))
        {
            Console.Error.WriteLine("Error: --backend daisinet requires --goal \"<instructions>\" (interactive mode is not supported for daisinet).");
            return 1;
        }

        HasFlagValue(args, "--model", out var model);
        HasFlagValue(args, "--role", out var role);
        int.TryParse(HasFlagValue(args, "--max-iterations", out var miRaw) ? miRaw : null, out var maxIter);
        var jsonOutput = args.Contains("--json");

        using var runner = new DaisinetGoalRunner(new DaisinetGoalRunner.Options
        {
            Goal = goal,
            MaxIterations = maxIter,
            Model = model,
            Role = role,
            JsonOutput = jsonOutput,
            WorkingDirectory = configManager.Config.WorkingDirectory ?? Directory.GetCurrentDirectory(),
        });

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await runner.RunAsync(cts.Token);
    }

    using var cli = new CliRunner(configManager, typeConfig);
    cli.ParseArgs(args);
    return await cli.RunAsync();
}

static bool HasFlagValue(string[] args, string name, out string? value)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
        {
            value = args[i + 1];
            return true;
        }
    }
    value = null;
    return false;
}

// TUI mode (default): full interactive terminal UI
// MinionEngine doesn't use the type system yet — it has its own full implementation
using var engine = new MinionEngine(configManager);
await engine.RunAsync();
return 0;
