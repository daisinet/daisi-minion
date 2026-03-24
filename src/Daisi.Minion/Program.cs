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
    using var cli = new CliRunner(configManager, typeConfig);
    cli.ParseArgs(args);
    return await cli.RunAsync();
}

// TUI mode (default): full interactive terminal UI
// MinionEngine doesn't use the type system yet — it has its own full implementation
using var engine = new MinionEngine(configManager);
await engine.RunAsync();
return 0;
