using Daisi.Minion.Modules;

namespace Daisi.Minion.Tests.Modules;

public class ModuleLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ModuleLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "module-loader-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    private const string ValidModuleSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Daisi.Minion.Modules;
        using Daisi.Minion.Coding;

        public class DiskModule : IMinionModule
        {
            public string Name => "disk-module";
            public string Description => "Loaded from disk";
            public void Initialize(MinionModuleContext context) { }
            public string? ExtendSystemPrompt() => "From disk";
            public IEnumerable<IMinionTool>? GetTools() => null;
            public string? PreProcess(string input) => null;
            public string? PostProcess(string response) => null;
            public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                Task.FromResult(new ModuleEvaluation { Score = 1.0 });
        }
        """;

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmpty()
    {
        var loader = new ModuleLoader(_tempDir);
        var modules = loader.LoadAll();
        Assert.Empty(modules);
    }

    [Fact]
    public void LoadAll_NonExistentDirectory_ReturnsEmpty()
    {
        var loader = new ModuleLoader(Path.Combine(_tempDir, "nonexistent"));
        var modules = loader.LoadAll();
        Assert.Empty(modules);
    }

    [Fact]
    public void LoadAll_ValidModule_LoadsSuccessfully()
    {
        var moduleDir = Path.Combine(_tempDir, "disk-module");
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, "module.cs"), ValidModuleSource);

        var loader = new ModuleLoader(_tempDir);
        var modules = loader.LoadAll();

        Assert.Single(modules);
        Assert.Equal("disk-module", modules[0].Name);
        Assert.Equal("From disk", modules[0].ExtendSystemPrompt());
    }

    [Fact]
    public void LoadAll_InvalidModule_SkipsWithoutCrash()
    {
        // One valid, one invalid
        var goodDir = Path.Combine(_tempDir, "good-module");
        Directory.CreateDirectory(goodDir);
        File.WriteAllText(Path.Combine(goodDir, "module.cs"), ValidModuleSource);

        var badDir = Path.Combine(_tempDir, "bad-module");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "module.cs"), "this is not valid C#");

        var errors = new List<string>();
        var loader = new ModuleLoader(_tempDir, log: msg => errors.Add(msg));
        var modules = loader.LoadAll();

        Assert.Single(modules);
        Assert.Equal("disk-module", modules[0].Name);
        Assert.Contains(errors, e => e.Contains("Failed"));
    }

    [Fact]
    public void LoadAll_ForbiddenApi_SkipsModule()
    {
        var badDir = Path.Combine(_tempDir, "unsafe-module");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "module.cs"), """
            using System.IO;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Daisi.Minion.Modules;
            using Daisi.Minion.Coding;

            public class UnsafeModule : IMinionModule
            {
                public string Name => "unsafe";
                public string Description => "Uses System.IO";
                public void Initialize(MinionModuleContext context) { }
                public string? ExtendSystemPrompt() => null;
                public IEnumerable<IMinionTool>? GetTools() => null;
                public string? PreProcess(string input) => null;
                public string? PostProcess(string response) => null;
                public Task<ModuleEvaluation> EvaluateAsync(TaskOutcome outcome) =>
                    Task.FromResult(new ModuleEvaluation { Score = 0.0 });
            }
            """);

        var loader = new ModuleLoader(_tempDir);
        var modules = loader.LoadAll();

        Assert.Empty(modules); // should be rejected by safety analyzer
    }

    [Fact]
    public void LoadAll_DirectoryWithoutModuleCs_Skipped()
    {
        var dir = Path.Combine(_tempDir, "no-module-file");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a module");

        var loader = new ModuleLoader(_tempDir);
        var modules = loader.LoadAll();

        Assert.Empty(modules);
    }

    [Fact]
    public void CompileFromSource_ValidSource_Succeeds()
    {
        var loader = new ModuleLoader(_tempDir);
        var result = loader.CompileFromSource(ValidModuleSource);

        Assert.True(result.Success);
        Assert.Single(result.Modules);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
