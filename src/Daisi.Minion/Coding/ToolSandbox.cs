namespace Daisi.Minion.Coding;

/// <summary>
/// Enforces a working directory boundary for tool execution.
/// All file paths are resolved relative to the sandbox root and rejected
/// if they escape it. Shell commands execute within the sandbox directory.
/// </summary>
public sealed class ToolSandbox
{
    /// <summary>The root directory of the sandbox. All paths must resolve within this.</summary>
    public string Root { get; }

    public ToolSandbox(string rootDirectory)
    {
        Root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(Root))
            throw new DirectoryNotFoundException($"Sandbox root does not exist: {Root}");
    }

    /// <summary>
    /// Resolve a path relative to the sandbox root and validate it stays within bounds.
    /// Returns the normalized absolute path.
    /// Throws InvalidOperationException if the path escapes the sandbox.
    /// </summary>
    public string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        // Resolve relative to sandbox root, not cwd
        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(Root, path));

        if (!IsWithinSandbox(resolved))
            throw new InvalidOperationException(
                $"Path escapes sandbox boundary: {path} (resolved to {resolved}, sandbox root: {Root})");

        return resolved;
    }

    /// <summary>
    /// Check if a resolved absolute path is within the sandbox root.
    /// </summary>
    public bool IsWithinSandbox(string absolutePath)
    {
        var normalizedPath = Path.GetFullPath(absolutePath);
        var normalizedRoot = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, Root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Try to resolve a path. Returns null if the path escapes the sandbox.
    /// </summary>
    public string? TryResolvePath(string path)
    {
        try { return ResolvePath(path); }
        catch { return null; }
    }
}
