using System.Reflection;

namespace Daisi.Minion.Engine;

/// <summary>
/// Loads personality trait definitions from ~/.daisi-minion/personas/.
/// Traits like "witty", "charming", "sarcastic" affect the minion's communication style.
/// Seeded from embedded Traits/ resources on first run.
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

    public IReadOnlyList<string> Available => _personas.Keys.OrderBy(k => k).ToList();
    public static string Directory => PersonasDir;
    public string? GetContent(string name) => _personas.TryGetValue(name, out var c) ? c : null;
    public bool Exists(string name) => _personas.ContainsKey(name);

    public void Reload()
    {
        _personas.Clear();
        LoadFromDisk();
    }

    private static void SeedBuiltInPersonas()
    {
        System.IO.Directory.CreateDirectory(PersonasDir);

        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Daisi.Minion.Traits.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            var fileName = resourceName[prefix.Length..];
            var filePath = Path.Combine(PersonasDir, fileName);

            if (File.Exists(filePath))
                continue;

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
