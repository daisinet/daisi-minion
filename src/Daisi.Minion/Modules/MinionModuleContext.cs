using Daisi.Minion.Coding;
using Daisi.Minion.Config;

namespace Daisi.Minion.Modules;

/// <summary>
/// Context provided to modules during initialization. Gives modules
/// access to the sandbox and configuration without exposing internals.
/// </summary>
public sealed class MinionModuleContext
{
    public string WorkingDirectory { get; init; } = "";
    public ToolSandbox Sandbox { get; init; } = null!;
    public string MinionName { get; init; } = "minion";
    public string? ActiveRole { get; init; }

    /// <summary>Log a diagnostic message from the module.</summary>
    public Action<string> Log { get; init; } = _ => { };
}
