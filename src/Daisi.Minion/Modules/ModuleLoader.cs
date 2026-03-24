namespace Daisi.Minion.Modules;

/// <summary>
/// Discovers and compiles modules from the modules directory.
/// Each module lives in its own subdirectory with a module.cs file.
/// </summary>
public sealed class ModuleLoader
{
    private static readonly string DefaultModulesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daisi-minion", "modules");

    private readonly string _modulesDir;
    private readonly ModuleCompiler _compiler = new();
    private readonly Action<string> _log;

    public ModuleLoader(string? modulesDir = null, Action<string>? log = null)
    {
        _modulesDir = modulesDir ?? DefaultModulesDir;
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// Load all modules from the modules directory.
    /// Logs and skips modules that fail to compile.
    /// </summary>
    public List<IMinionModule> LoadAll()
    {
        var modules = new List<IMinionModule>();

        if (!Directory.Exists(_modulesDir))
        {
            _log($"Modules directory not found: {_modulesDir}");
            return modules;
        }

        foreach (var dir in Directory.GetDirectories(_modulesDir))
        {
            var modulePath = Path.Combine(dir, "module.cs");
            if (!File.Exists(modulePath))
                continue;

            var moduleName = Path.GetFileName(dir);
            _log($"Compiling module: {moduleName}...");

            var result = _compiler.Compile(modulePath);
            if (result.Success)
            {
                modules.AddRange(result.Modules);
                _log($"  Loaded {result.Modules.Count} module(s) from {moduleName}");
            }
            else
            {
                _log($"  Failed to compile {moduleName}:");
                foreach (var error in result.Errors)
                    _log($"    {error}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Load a single module from a specific source file.
    /// </summary>
    public ModuleCompileResult LoadFromFile(string sourcePath)
    {
        return _compiler.Compile(sourcePath);
    }

    /// <summary>
    /// Compile a module from source code string (for testing/Darwin).
    /// </summary>
    public ModuleCompileResult CompileFromSource(string sourceCode)
    {
        return _compiler.CompileFromSource(sourceCode);
    }
}
