using Daisi.Minion.Coding;

namespace Daisi.Minion.Types;

/// <summary>
/// Defines a minion type's behavior: what tools it has, what its system prompt
/// says, and how it evaluates completion. Types are composed with a runner
/// (TUI or CLI) — the type defines *what*, the runner defines *how*.
/// </summary>
public sealed class MinionTypeConfig
{
    /// <summary>Type name (code, test, research, summoner, darwin).</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Description { get; init; }

    /// <summary>Default role if none is configured.</summary>
    public string DefaultRole { get; init; } = "chat";

    /// <summary>Additional text appended to the system prompt.</summary>
    public string? SystemPromptExtension { get; init; }

    /// <summary>
    /// Tool filter. If set, only tools whose names are in this set will be registered.
    /// Null means all base tools are available.
    /// </summary>
    public HashSet<string>? AllowedTools { get; init; }

    /// <summary>
    /// Additional tools to register beyond the base set.
    /// Created lazily via factory so sandbox can be injected.
    /// </summary>
    public Func<ToolSandbox, IEnumerable<IMinionTool>>? AdditionalToolsFactory { get; init; }
}
