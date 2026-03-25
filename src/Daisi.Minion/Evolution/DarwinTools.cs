using System.Text.Json.Nodes;
using Daisi.Minion.Coding;

namespace Daisi.Minion.Evolution;

/// <summary>Create a new module from source code.</summary>
public sealed class CreateModuleTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    public CreateModuleTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "create_module";
    public string Description => "Create a new module. Provide the module name and C# source code implementing IMinionModule. Optionally provide test source.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name (e.g. 'react-reviewer')" },
            ["module_source"] = new JsonObject { ["type"] = "string", ["description"] = "C# source code implementing IMinionModule" },
            ["test_source"] = new JsonObject { ["type"] = "string", ["description"] = "Optional C# test source code" },
        },
        ["required"] = new JsonArray("name", "module_source"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name");
        var moduleSource = ToolArgs.GetString(arguments, "module_source");
        var testSource = ToolArgs.GetString(arguments, "test_source");

        if (string.IsNullOrEmpty(name)) return Task.FromResult(ToolResult.Error("Missing: name"));
        if (string.IsNullOrEmpty(moduleSource)) return Task.FromResult(ToolResult.Error("Missing: module_source"));

        var result = _evolver.CreateModule(name, moduleSource, testSource);
        return Task.FromResult(result.Success
            ? ToolResult.Success($"Created module '{name}' at {result.ModulePath}")
            : ToolResult.Error($"Failed at {result.Phase}: {string.Join("; ", result.Errors.Take(3))}"));
    }
}

/// <summary>Compile and validate a module without writing to disk.</summary>
public sealed class CompileModuleTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    public CompileModuleTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "compile_module";
    public string Description => "Compile and validate a module source without saving. Returns compile errors or success.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name for baseline comparison" },
            ["module_source"] = new JsonObject { ["type"] = "string", ["description"] = "C# source code to compile" },
            ["test_source"] = new JsonObject { ["type"] = "string", ["description"] = "Optional test source to validate" },
        },
        ["required"] = new JsonArray("name", "module_source"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name") ?? "unnamed";
        var moduleSource = ToolArgs.GetString(arguments, "module_source");
        var testSource = ToolArgs.GetString(arguments, "test_source");

        if (string.IsNullOrEmpty(moduleSource)) return Task.FromResult(ToolResult.Error("Missing: module_source"));

        var result = _evolver.Validate(name, moduleSource, testSource);
        return Task.FromResult(result.Success
            ? ToolResult.Success(result.Summary())
            : ToolResult.Error(result.Summary()));
    }
}

/// <summary>Run a module's test suite.</summary>
public sealed class TestModuleTool : IMinionTool
{
    private readonly ModuleTestRunner _testRunner = new();
    public string Name => "test_module";
    public string Description => "Compile and run a module's test suite. Returns pass/fail results for each test.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["module_source"] = new JsonObject { ["type"] = "string", ["description"] = "Module C# source" },
            ["test_source"] = new JsonObject { ["type"] = "string", ["description"] = "Test C# source" },
        },
        ["required"] = new JsonArray("module_source", "test_source"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var moduleSource = ToolArgs.GetString(arguments, "module_source");
        var testSource = ToolArgs.GetString(arguments, "test_source");

        if (string.IsNullOrEmpty(moduleSource)) return Task.FromResult(ToolResult.Error("Missing: module_source"));
        if (string.IsNullOrEmpty(testSource)) return Task.FromResult(ToolResult.Error("Missing: test_source"));

        var result = _testRunner.RunTests(moduleSource, testSource);

        var output = result.Summary();
        if (result.TestCases.Count > 0)
        {
            output += "\n" + string.Join("\n", result.TestCases.Select(t =>
                t.Passed ? $"  PASS {t.Name}" : $"  FAIL {t.Name}: {t.Error}"));
        }

        return Task.FromResult(result.Success ? ToolResult.Success(output) : ToolResult.Error(output));
    }
}

/// <summary>Read a module's current source and evaluation history.</summary>
public sealed class ReadModuleTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    public ReadModuleTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "read_module";
    public string Description => "Read a module's current source code and evaluation history.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name to read" },
        },
        ["required"] = new JsonArray("name"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name");
        if (string.IsNullOrEmpty(name)) return Task.FromResult(ToolResult.Error("Missing: name"));

        var (moduleSource, testSource) = _evolver.ReadModule(name);
        if (moduleSource == null)
            return Task.FromResult(ToolResult.Error($"Module '{name}' not found."));

        var output = $"--- module.cs ---\n{moduleSource}";
        if (testSource != null)
            output += $"\n\n--- tests.cs ---\n{testSource}";

        output += $"\n\n--- evaluation ---\n{_evolver.GetEvaluationSummary(name)}";

        return Task.FromResult(ToolResult.Success(output));
    }
}

/// <summary>Commit a validated module version to disk.</summary>
public sealed class CommitModuleTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    public CommitModuleTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "commit_module";
    public string Description => "Commit a validated module version to disk, replacing the current one.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name" },
            ["module_source"] = new JsonObject { ["type"] = "string", ["description"] = "Module C# source" },
            ["test_source"] = new JsonObject { ["type"] = "string", ["description"] = "Optional test source" },
        },
        ["required"] = new JsonArray("name", "module_source"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name");
        var moduleSource = ToolArgs.GetString(arguments, "module_source");
        var testSource = ToolArgs.GetString(arguments, "test_source");

        if (string.IsNullOrEmpty(name)) return Task.FromResult(ToolResult.Error("Missing: name"));
        if (string.IsNullOrEmpty(moduleSource)) return Task.FromResult(ToolResult.Error("Missing: module_source"));

        // Validate before committing
        var validation = _evolver.Validate(name, moduleSource, testSource);
        if (!validation.Success)
            return Task.FromResult(ToolResult.Error($"Validation failed: {validation.Summary()}"));

        _evolver.Commit(name, moduleSource, testSource);
        return Task.FromResult(ToolResult.Success($"Committed module '{name}'."));
    }
}

/// <summary>List all available modules with their scores.</summary>
public sealed class ListModulesTool : IMinionTool
{
    private readonly ModuleEvolver _evolver;
    public ListModulesTool(ModuleEvolver evolver) => _evolver = evolver;

    public string Name => "list_modules";
    public string Description => "List all available modules with their evaluation scores.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var modules = _evolver.ListModules();
        if (modules.Count == 0)
            return Task.FromResult(ToolResult.Success("No modules found."));

        var output = string.Join("\n", modules.Select(name =>
        {
            var summary = _evolver.GetEvaluationSummary(name);
            return $"- {name}: {summary.Split('\n').FirstOrDefault() ?? "no data"}";
        }));

        return Task.FromResult(ToolResult.Success(output));
    }
}
