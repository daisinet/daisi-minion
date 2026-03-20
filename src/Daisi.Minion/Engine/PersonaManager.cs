using System.Reflection;

namespace Daisi.Minion.Engine;

/// <summary>
/// Loads persona definitions from embedded markdown resources.
/// </summary>
public sealed class PersonaManager
{
    private readonly Dictionary<string, string> _personas = new(StringComparer.OrdinalIgnoreCase);

    public PersonaManager()
    {
        LoadEmbeddedPersonas();
    }

    /// <summary>All available persona names.</summary>
    public IReadOnlyList<string> Available => _personas.Keys.OrderBy(k => k).ToList();

    /// <summary>Get the markdown content for a persona, or null if not found.</summary>
    public string? GetContent(string name) =>
        _personas.TryGetValue(name, out var content) ? content : null;

    /// <summary>Check if a persona exists.</summary>
    public bool Exists(string name) => _personas.ContainsKey(name);

    private void LoadEmbeddedPersonas()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "Daisi.Minion.Personas.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            var name = resourceName[prefix.Length..^3]; // strip prefix and .md
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            _personas[name] = reader.ReadToEnd();
        }
    }
}
