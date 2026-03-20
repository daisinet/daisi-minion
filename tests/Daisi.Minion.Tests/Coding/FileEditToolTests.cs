using System.Text.Json.Nodes;
using Daisi.Minion.Coding.Tools;

namespace Daisi.Minion.Tests.Coding;

public class FileEditToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileEditTool _tool = new();

    public FileEditToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"daisi-minion-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ReplacesUniqueString()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "Hello world\nGoodbye world\n");

        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = file,
            ["old_string"] = "Hello world",
            ["new_string"] = "Hi world",
        }, CancellationToken.None);

        Assert.False(result.IsError);
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("Hi world", content);
        Assert.Contains("Goodbye world", content);
    }

    [Fact]
    public async Task FailsOnNonUniqueMatch()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "hello\nhello\n");

        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = file,
            ["old_string"] = "hello",
            ["new_string"] = "world",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("multiple", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplaceAllReplacesMultiple()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "hello\nhello\nhello\n");

        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = file,
            ["old_string"] = "hello",
            ["new_string"] = "world",
            ["replace_all"] = true,
        }, CancellationToken.None);

        Assert.False(result.IsError);
        var content = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("hello", content);
        Assert.Contains("3 occurrence", result.Output);
    }

    [Fact]
    public async Task FailsOnMissingFile()
    {
        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = Path.Combine(_tempDir, "nonexistent.txt"),
            ["old_string"] = "a",
            ["new_string"] = "b",
        }, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task FailsWhenStringNotFound()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "hello world\n");

        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = file,
            ["old_string"] = "not here",
            ["new_string"] = "replaced",
        }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Output);
    }
}
