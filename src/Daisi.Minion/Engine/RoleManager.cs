using System.Reflection;

namespace Daisi.Minion.Engine;

/// <summary>
/// Loads role definitions from ~/.daisi-minion/roles/.
/// On first run, seeds the folder with built-in roles from embedded resources.
/// Users can edit existing roles or drop new .md files into the folder.
/// </summary>
public sealed class RoleManager
{
    private readonly Dictionary<string, string> _roles = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string RolesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "roles");

    public RoleManager()
    {
        SeedBuiltInRoles();
        LoadFromDisk();
    }

    public IReadOnlyList<string> Available => _roles.Keys.OrderBy(k => k).ToList();
    public static string Directory => RolesDir;
    public string? GetContent(string name) => _roles.TryGetValue(name, out var c) ? c : null;
    public bool Exists(string name) => _roles.ContainsKey(name);

    public void Reload()
    {
        _roles.Clear();
        LoadFromDisk();
    }

    private static void SeedBuiltInRoles()
    {
        System.IO.Directory.CreateDirectory(RolesDir);

        var assembly = Assembly.GetExecutingAssembly();
        // Roles are embedded under Personas/ (the original resource folder)
        var prefix = "Daisi.Minion.Personas.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            var fileName = resourceName[prefix.Length..];
            var filePath = Path.Combine(RolesDir, fileName);

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
        if (!System.IO.Directory.Exists(RolesDir))
            return;

        foreach (var file in System.IO.Directory.EnumerateFiles(RolesDir, "*.md"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            _roles[name] = File.ReadAllText(file);
        }
    }
}
