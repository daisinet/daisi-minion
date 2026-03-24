using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Daisi.Minion.Modules;

/// <summary>
/// Compiles module .cs source files via Roslyn. Runs safety analysis to reject
/// forbidden API usage, then compiles against allowed references and loads
/// the resulting assembly into an isolated AssemblyLoadContext.
/// </summary>
public sealed class ModuleCompiler
{
    private readonly List<MetadataReference> _references;

    public ModuleCompiler()
    {
        _references = BuildReferences();
    }

    /// <summary>
    /// Compile a module from source code.
    /// Returns the compiled module instances or a list of errors.
    /// </summary>
    public ModuleCompileResult Compile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return ModuleCompileResult.Failure([$"Source file not found: {sourcePath}"]);

        var sourceText = File.ReadAllText(sourcePath);
        return CompileFromSource(sourceText, sourcePath);
    }

    /// <summary>
    /// Compile a module from a source string (for testing).
    /// </summary>
    public ModuleCompileResult CompileFromSource(string sourceCode, string? sourcePath = null)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode, path: sourcePath ?? "module.cs");

        // Phase 1: Safety analysis
        var violations = SafetyAnalyzer.Analyze(tree);
        if (violations.Count > 0)
        {
            var errors = violations.Select(v => v.GetMessage()).ToList();
            return ModuleCompileResult.Failure(errors);
        }

        // Phase 2: Compilation
        var assemblyName = $"MinionModule_{Guid.NewGuid():N}";
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [tree],
            references: _references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithAllowUnsafe(false)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())
                .ToList();
            return ModuleCompileResult.Failure(errors);
        }

        // Phase 3: Load into isolated context
        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Phase 4: Find and instantiate IMinionModule implementations
        var modules = new List<IMinionModule>();
        foreach (var type in assembly.GetExportedTypes())
        {
            if (typeof(IMinionModule).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IMinionModule module)
                        modules.Add(module);
                }
                catch (Exception ex)
                {
                    return ModuleCompileResult.Failure([$"Failed to instantiate {type.Name}: {ex.Message}"]);
                }
            }
        }

        if (modules.Count == 0)
            return ModuleCompileResult.Failure(["No IMinionModule implementations found in source."]);

        return ModuleCompileResult.Ok(modules, loadContext);
    }

    private static List<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();

        // Core runtime assemblies
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies != null)
        {
            // Include only essential runtime assemblies
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Runtime",
                "System.Collections",
                "System.Linq",
                "System.Text.Json",
                "System.Text.RegularExpressions",
                "System.Threading",
                "System.Threading.Tasks",
                "netstandard",
                "System.Private.CoreLib",
                "System.ObjectModel",
                "System.ComponentModel",
            };

            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (allowed.Contains(name))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Add our own assemblies so modules can reference IMinionModule, IMinionTool, etc.
        refs.Add(MetadataReference.CreateFromFile(typeof(IMinionModule).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Coding.IMinionTool).Assembly.Location));

        return refs;
    }
}

/// <summary>
/// Result of compiling a module. Either success with module instances,
/// or failure with error messages.
/// </summary>
public sealed class ModuleCompileResult
{
    public bool Success { get; private init; }
    public IReadOnlyList<IMinionModule> Modules { get; private init; } = [];
    public IReadOnlyList<string> Errors { get; private init; } = [];
    public AssemblyLoadContext? LoadContext { get; private init; }

    public static ModuleCompileResult Ok(List<IMinionModule> modules, AssemblyLoadContext loadContext) =>
        new() { Success = true, Modules = modules, LoadContext = loadContext };

    public static ModuleCompileResult Failure(List<string> errors) =>
        new() { Success = false, Errors = errors };
}
