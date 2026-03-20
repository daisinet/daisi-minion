using System.Text.Json;

namespace Daisi.Minion.Config;

/// <summary>
/// Manages the daisi-minion configuration file at ~/.daisi-minion/config.json.
/// </summary>
public sealed class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public MinionConfig Config { get; private set; } = new();

    /// <summary>
    /// Load config from disk, or create defaults if not present.
    /// </summary>
    public void Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<MinionConfig>(json, JsonOptions) ?? new MinionConfig();
        }
        else
        {
            Config = new MinionConfig();
            // Try to find a default model
            if (Directory.Exists(Config.ModelsDirectory))
            {
                var gguf = Directory.EnumerateFiles(Config.ModelsDirectory, "*.gguf").FirstOrDefault();
                if (gguf != null)
                    Config.ActiveModel = gguf;
            }
        }
    }

    /// <summary>
    /// Save current config to disk.
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(Config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
