namespace Daisi.Minion.Types;

/// <summary>
/// Factory that maps type names to MinionTypeConfig instances.
/// </summary>
public static class MinionTypeFactory
{
    private static readonly Dictionary<string, MinionTypeConfig> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = new MinionTypeConfig
        {
            Name = "code",
            Description = "Code-focused minion for writing, fixing, and refactoring code",
            DefaultRole = "coder",
            SystemPromptExtension = """
                You are a coding assistant. Focus on writing, fixing, and refactoring code.
                Always read files before editing them. Run tests after making changes when possible.
                """,
        },

        ["test"] = new MinionTypeConfig
        {
            Name = "test",
            Description = "Test-focused minion for writing and running tests",
            DefaultRole = "coder",
            SystemPromptExtension = """
                You are a testing specialist. Your job is to write comprehensive tests,
                run test suites, analyze failures, and fix code to make tests pass.
                Focus on edge cases, error handling, and code coverage.
                Always run tests after writing them to verify they pass.
                """,
        },

        ["research"] = new MinionTypeConfig
        {
            Name = "research",
            Description = "Read-only minion for codebase exploration and analysis",
            DefaultRole = "chat",
            SystemPromptExtension = """
                You are a research assistant. Your job is to explore, read, search,
                and analyze code. Summarize your findings clearly.
                You do NOT have write access — you cannot create, edit, or delete files.
                You cannot run shell commands. Focus on reading and understanding.
                """,
            AllowedTools = ["file_read", "grep", "glob", "git"],
        },

        ["summoner"] = new MinionTypeConfig
        {
            Name = "summoner",
            Description = "Orchestrator minion that spawns and coordinates other minions",
            DefaultRole = "chat",
            SystemPromptExtension = """
                You are a Summoner — a coordinator that manages worker minions.
                Break complex tasks into subtasks and assign each to a specialized minion.

                Available minion types: code (writing/fixing code), test (writing/running tests), research (read-only exploration).

                Strategy:
                - Decompose the goal into independent subtasks
                - Spawn a typed minion for each subtask with a clear, specific task description
                - Monitor progress with check_minion and list_minions
                - Send follow-up messages with send_message if a minion needs guidance
                - Stop minions when they're done to free resources
                - Prefer 2-3 focused minions over one doing everything
                """,
            // Summoner uses orchestration tools instead of file tools.
            // Base file tools are excluded — the summoner delegates, not codes.
            AllowedTools = [],
        },
        ["darwin"] = new MinionTypeConfig
        {
            Name = "darwin",
            Description = "Evolution minion that creates, tests, and improves other modules",
            DefaultRole = "coder",
            SystemPromptExtension = """
                You are Darwin — an evolution minion. Your purpose is to create, test, and improve modules
                that make other minions better.

                Your workflow (the fast loop):
                1. Read the current module source and evaluation history with read_module
                2. Identify weaknesses from the evaluation data
                3. Write improved module source code implementing IMinionModule
                4. Compile and validate with compile_module (catches safety violations and compile errors)
                5. Run tests with test_module (all tests must pass)
                6. If validation passes, commit with commit_module
                7. If it fails, analyze errors and try a different approach

                Rules:
                - Modules must implement IMinionModule from Daisi.Minion.Modules
                - Modules cannot use System.IO, System.Diagnostics, System.Net, or System.Reflection directly
                - Modules interact with files and processes only through IMinionTool interfaces
                - Always write tests alongside modules
                - Test methods must be public, return Task, and start with "Test"
                - Never weaken tests to make scores look better
                """,
            // Darwin uses evolution tools, not file tools — it works on modules, not code files
            AllowedTools = [],
        },
    };

    /// <summary>Get the type config by name. Throws on unknown type.</summary>
    public static MinionTypeConfig Get(string typeName)
    {
        if (Types.TryGetValue(typeName, out var config))
            return config;
        throw new ArgumentException($"Unknown minion type: {typeName}. Available: {string.Join(", ", Types.Keys)}");
    }

    /// <summary>Check if a type name is registered.</summary>
    public static bool Exists(string typeName) => Types.ContainsKey(typeName);

    /// <summary>Get all registered type names.</summary>
    public static IReadOnlyList<string> Available => Types.Keys.OrderBy(k => k).ToList();

    /// <summary>Register a new type (for future extensibility).</summary>
    public static void Register(MinionTypeConfig config) => Types[config.Name] = config;
}
