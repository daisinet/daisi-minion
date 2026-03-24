using Daisi.Minion.Coding;

namespace Daisi.Minion.Tests.Coding;

public class ToolSandboxTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ToolSandbox _sandbox;

    public ToolSandboxTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sandbox-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _sandbox = new ToolSandbox(_tempDir);
    }

    [Fact]
    public void ResolvePath_RelativePath_ResolvesWithinSandbox()
    {
        var resolved = _sandbox.ResolvePath("subdir/file.txt");
        Assert.StartsWith(_tempDir, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("file.txt", resolved);
    }

    [Fact]
    public void ResolvePath_AbsolutePathWithinSandbox_Allowed()
    {
        var innerPath = Path.Combine(_tempDir, "inner", "file.txt");
        var resolved = _sandbox.ResolvePath(innerPath);
        Assert.Equal(Path.GetFullPath(innerPath), resolved);
    }

    [Fact]
    public void ResolvePath_DotDotEscape_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _sandbox.ResolvePath("../../etc/passwd"));
    }

    [Fact]
    public void ResolvePath_AbsolutePathOutsideSandbox_Throws()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside", "file.txt");
        Assert.Throws<InvalidOperationException>(() =>
            _sandbox.ResolvePath(outsidePath));
    }

    [Fact]
    public void ResolvePath_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => _sandbox.ResolvePath(""));
    }

    [Fact]
    public void ResolvePath_SandboxRootItself_Allowed()
    {
        // Resolving "." should give the sandbox root
        var resolved = _sandbox.ResolvePath(".");
        Assert.Equal(Path.GetFullPath(_tempDir), resolved);
    }

    [Fact]
    public void IsWithinSandbox_PathInside_ReturnsTrue()
    {
        var inner = Path.Combine(_tempDir, "src", "main.cs");
        Assert.True(_sandbox.IsWithinSandbox(inner));
    }

    [Fact]
    public void IsWithinSandbox_PathOutside_ReturnsFalse()
    {
        Assert.False(_sandbox.IsWithinSandbox(@"C:\Windows\System32\cmd.exe"));
    }

    [Fact]
    public void IsWithinSandbox_RootItself_ReturnsTrue()
    {
        Assert.True(_sandbox.IsWithinSandbox(_tempDir));
    }

    [Fact]
    public void TryResolvePath_ValidPath_ReturnsResolved()
    {
        var result = _sandbox.TryResolvePath("file.txt");
        Assert.NotNull(result);
        Assert.Contains("file.txt", result);
    }

    [Fact]
    public void TryResolvePath_EscapingPath_ReturnsNull()
    {
        var result = _sandbox.TryResolvePath("../../etc/passwd");
        Assert.Null(result);
    }

    [Fact]
    public void Constructor_NonExistentDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new ToolSandbox(Path.Combine(_tempDir, "nonexistent")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
