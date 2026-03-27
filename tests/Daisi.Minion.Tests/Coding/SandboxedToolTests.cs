using System.Text.Json.Nodes;
using Daisi.Minion.Coding;
using Daisi.Minion.Coding.Tools;

namespace Daisi.Minion.Tests.Coding;

public class SandboxedToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ToolSandbox _sandbox;

    public SandboxedToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sandbox-tool-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _sandbox = new ToolSandbox(_tempDir);
    }

    // --- FileReadTool ---

    [Fact]
    public async Task FileRead_InsideSandbox_Succeeds()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "hello world");
        var tool = new FileReadTool(_sandbox);

        var result = await tool.ExecuteAsync(new JsonObject { ["path"] = "test.txt" }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("hello world", result.Output);
    }

    [Fact]
    public async Task FileRead_OutsideSandbox_Blocked()
    {
        var tool = new FileReadTool(_sandbox);
        var result = await tool.ExecuteAsync(
            new JsonObject { ["path"] = "../../etc/passwd" }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("escapes sandbox", result.Output);
    }

    [Fact]
    public async Task FileRead_AbsolutePathOutside_Blocked()
    {
        var tool = new FileReadTool(_sandbox);
        var outside = Path.Combine(Path.GetTempPath(), "outside.txt");
        var result = await tool.ExecuteAsync(
            new JsonObject { ["path"] = outside }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("escapes sandbox", result.Output);
    }

    // --- FileWriteTool ---

    [Fact]
    public async Task FileWrite_InsideSandbox_Succeeds()
    {
        var tool = new FileWriteTool(_sandbox);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["path"] = "output.txt",
            ["content"] = "written by test",
        }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_tempDir, "output.txt")));
        Assert.Equal("written by test", File.ReadAllText(Path.Combine(_tempDir, "output.txt")));
    }

    [Fact]
    public async Task FileWrite_OutsideSandbox_Blocked()
    {
        var tool = new FileWriteTool(_sandbox);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["path"] = "../../escape.txt",
            ["content"] = "should not exist",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("escapes sandbox", result.Output);
    }

    [Fact]
    public async Task FileWrite_CreatesSubdirectory()
    {
        var tool = new FileWriteTool(_sandbox);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["path"] = "sub/dir/file.txt",
            ["content"] = "nested",
        }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_tempDir, "sub", "dir", "file.txt")));
    }

    // --- FileEditTool ---

    [Fact]
    public async Task FileEdit_InsideSandbox_Succeeds()
    {
        var file = Path.Combine(_tempDir, "edit.txt");
        await File.WriteAllTextAsync(file, "old content here");
        var tool = new FileEditTool(_sandbox);

        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["path"] = "edit.txt",
            ["old_string"] = "old content",
            ["new_string"] = "new content",
        }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("new content here", File.ReadAllText(file));
    }

    [Fact]
    public async Task FileEdit_OutsideSandbox_Blocked()
    {
        var tool = new FileEditTool(_sandbox);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["path"] = "../../escape.txt",
            ["old_string"] = "a",
            ["new_string"] = "b",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("escapes sandbox", result.Output);
    }

    // --- GrepTool ---

    [Fact]
    public async Task Grep_DefaultsToSandboxRoot()
    {
        var file = Path.Combine(_tempDir, "search.txt");
        await File.WriteAllTextAsync(file, "findme123\nnotthis\n");
        var tool = new GrepTool(_sandbox);

        var result = await tool.ExecuteAsync(
            new JsonObject { ["pattern"] = "findme" }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("findme123", result.Output);
    }

    [Fact]
    public async Task Grep_OutsideSandbox_Blocked()
    {
        var tool = new GrepTool(_sandbox);
        var result = await tool.ExecuteAsync(new JsonObject
        {
            ["pattern"] = "test",
            ["path"] = "../../..",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("escapes sandbox", result.Output);
    }

    // --- GlobTool ---

    [Fact]
    public async Task Glob_DefaultsToSandboxRoot()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.cs"), "");
        var tool = new GlobTool(_sandbox);

        var result = await tool.ExecuteAsync(
            new JsonObject { ["pattern"] = "*.cs" }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("a.cs", result.Output);
        Assert.Contains("b.cs", result.Output);
    }

    // --- ShellExecuteTool ---

    [Fact]
    public async Task Shell_RunsInSandboxDirectory()
    {
        var tool = new ShellExecuteTool(_sandbox);
        var command = OperatingSystem.IsWindows() ? "cd" : "pwd";

        var result = await tool.ExecuteAsync(
            new JsonObject { ["command"] = command }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(_tempDir, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // --- GitTool ---

    [Fact]
    public async Task Git_RunsInSandboxDirectory()
    {
        // Init a git repo in sandbox
        var tool = new GitTool(_sandbox);
        var initResult = await tool.ExecuteAsync(
            new JsonObject { ["args"] = "init" }, CancellationToken.None);

        // git status should work in the sandbox
        var result = await tool.ExecuteAsync(
            new JsonObject { ["args"] = "status" }, CancellationToken.None);

        Assert.False(result.IsError);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
