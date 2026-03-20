using Daisi.Minion.Config;
using Daisi.Minion.Engine;

// Load configuration
var configManager = new ConfigManager();
configManager.Load();

// Run the main engine
using var engine = new MinionEngine(configManager);
await engine.RunAsync();
