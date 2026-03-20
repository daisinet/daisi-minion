using System.Reflection;

namespace Daisi.Minion.Engine;

/// <summary>
/// Loads persona definitions from ~/.daisi-minion/personas/.
/// On first run, seeds the folder with built-in personas from embedded resources.
/// Users can edit existing personas or drop new .md files into the folder.
/// </summary>
public sealed class PersonaManager
{
    private readonly Dictionary<string, string> _personas = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string PersonasDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "personas");

    public PersonaManager()
    {
        SeedBuiltInPersonas();
        LoadFromDisk();
    }

    /// <summary>All available persona names.</summary>
    public IReadOnlyList<string> Available => _personas.Keys.OrderBy(k => k).ToList();

    /// <summary>The personas directory path.</summary>
    public static string Directory => PersonasDir;

    /// <summary>Get the markdown content for a persona, or null if not found.</summary>
    public string? GetContent(string name) =>
        _personas.TryGetValue(name, out var content) ? content : null;

    /// <summary>Check if a persona exists.</summary>
    public bool Exists(string name) => _personas.ContainsKey(name);

    /// <summary>Reload all personas from disk.</summary>
    public void Reload()
    {
        _personas.Clear();
        LoadFromDisk();
    }

    /// <summary>
    /// Seed the personas directory with built-in defaults.
    /// Only writes files that don't already exist, so user edits are preserved.
    /// </summary>
    private static void SeedBuiltInPersonas()
    {
        System.IO.Directory.CreateDirectory(PersonasDir);

        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Daisi.Minion.Personas.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            var fileName = resourceName[prefix.Length..]; // e.g. "coder.md"
            var filePath = Path.Combine(PersonasDir, fileName);

            if (File.Exists(filePath))
                continue; // Don't overwrite user edits

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            File.WriteAllText(filePath, reader.ReadToEnd());
        }
    }

    private void LoadFromDisk()
    {
        if (!System.IO.Directory.Exists(PersonasDir))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(PersonasDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            _personas[name] = File.ReadAllText(file);
        }
    }
}
