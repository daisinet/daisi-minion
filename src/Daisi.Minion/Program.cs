using Daisi.Minion.Config;
using Daisi.Minion.Engine;

// Load configuration
var configManager = new ConfigManager();
configManager.Load();

// CLI mode: headless, no TUI, plain text to stdout/stderr
if (args.Contains("--cli"))
{
    using var cli = new CliRunner(configManager);
    cli.ParseArgs(args);
    return await cli.RunAsync();
}

// TUI mode (default): full interactive terminal UI
using var engine = new MinionEngine(configManager);
await engine.RunAsync();
return 0;
