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
