using System.Text.Json.Nodes;
using Daisi.Minion.Coding;
using Daisi.Minion.Config;
using Daisi.Minion.Modules;

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

/// <summary>
/// Start a Darwin evolution run — creates a branch on the remote fork.
/// All subsequent push_module calls go to this branch.
/// </summary>
public sealed class StartEvolutionRunTool : IMinionTool
{
    private readonly ConfigManager _configManager;
    public StartEvolutionRunTool(ConfigManager configManager) => _configManager = configManager;

    public string Name => "start_evolution_run";
    public string Description => "Create a new Darwin branch on the remote fork for this evolution session. Returns the branch name. Call this before push_module.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var config = _configManager.Config;
        if (string.IsNullOrEmpty(config.DaisiGitServer) || string.IsNullOrEmpty(config.DaisiGitToken)
            || string.IsNullOrEmpty(config.ModulesRepo))
            return ToolResult.Error("DaisiGit not configured.");

        try
        {
            using var source = CreateSource(config);
            var branch = await source.CreateDarwinBranchAsync(config.MinionName, ct);

            // Store the active branch for push_module to use
            _configManager.Config.ModulesBranch = branch;

            return ToolResult.Success($"Created evolution branch: {branch}\nAll push_module calls will target this branch.");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed: {ex.Message}");
        }
    }

    private static ModuleRemoteSource CreateSource(Config.MinionConfig config) =>
        new(config.DaisiGitServer!, config.DaisiGitToken!, config.ModulesRepo!, config.ModulesBranch);
}

/// <summary>Push an evolved module to the current Darwin branch on the fork.</summary>
public sealed class PushModuleTool : IMinionTool
{
    private readonly ConfigManager _configManager;
    public PushModuleTool(ConfigManager configManager) => _configManager = configManager;

    public string Name => "push_module";
    public string Description => "Push a committed module to the remote DaisiGit fork. Call start_evolution_run first to create a branch.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Module name to push" },
        },
        ["required"] = new JsonArray("name"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var name = ToolArgs.GetString(arguments, "name");
        if (string.IsNullOrEmpty(name)) return ToolResult.Error("Missing: name");

        var config = _configManager.Config;
        if (string.IsNullOrEmpty(config.DaisiGitServer) || string.IsNullOrEmpty(config.DaisiGitToken)
            || string.IsNullOrEmpty(config.ModulesRepo))
            return ToolResult.Error("DaisiGit not configured.");

        try
        {
            using var source = new ModuleRemoteSource(
                config.DaisiGitServer, config.DaisiGitToken, config.ModulesRepo,
                config.ModulesBranch);

            await source.PushModuleAsync(name, config.ModulesBranch, ct);
            return ToolResult.Success($"Pushed '{name}' to {config.ModulesRepo}@{config.ModulesBranch}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Push failed: {ex.Message}");
        }
    }
}

/// <summary>Create a PR from the Darwin branch back to main for review/merge.</summary>
public sealed class SubmitEvolutionPrTool : IMinionTool
{
    private readonly ConfigManager _configManager;
    public SubmitEvolutionPrTool(ConfigManager configManager) => _configManager = configManager;

    public string Name => "submit_evolution_pr";
    public string Description => "Create a pull request from the current Darwin branch back to main. Call after pushing evolved modules.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["title"] = new JsonObject { ["type"] = "string", ["description"] = "PR title summarizing the evolution" },
            ["description"] = new JsonObject { ["type"] = "string", ["description"] = "What changed and why" },
        },
        ["required"] = new JsonArray("title"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var title = ToolArgs.GetString(arguments, "title");
        var desc = ToolArgs.GetString(arguments, "description");
        if (string.IsNullOrEmpty(title)) return ToolResult.Error("Missing: title");

        var config = _configManager.Config;
        if (string.IsNullOrEmpty(config.DaisiGitServer) || string.IsNullOrEmpty(config.DaisiGitToken)
            || string.IsNullOrEmpty(config.ModulesRepo))
            return ToolResult.Error("DaisiGit not configured.");

        if (!config.ModulesBranch.StartsWith("darwin/"))
            return ToolResult.Error("Not on a Darwin branch. Call start_evolution_run first.");

        try
        {
            using var source = new ModuleRemoteSource(
                config.DaisiGitServer, config.DaisiGitToken, config.ModulesRepo, "main");

            var prNumber = await source.CreatePullRequestAsync(config.ModulesBranch, title, desc, ct);
            return ToolResult.Success($"Created PR #{prNumber}: {title}\nBranch {config.ModulesBranch} → main");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"PR failed: {ex.Message}");
        }
    }
}
