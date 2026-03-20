using System.Text.Json.Nodes;
using Daisi.Minion.Coding.Tools;

namespace Daisi.Minion.Tests.Coding;

public class FileReadToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileReadTool _tool = new();

    public FileReadToolTests()
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
    public async Task ReadsFileWithLineNumbers()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "line one\nline two\nline three\n");

        var result = await _tool.ExecuteAsync(new JsonObject { ["path"] = file }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("1\tline one", result.Output);
        Assert.Contains("2\tline two", result.Output);
        Assert.Contains("3\tline three", result.Output);
    }

    [Fact]
    public async Task RespectsOffsetAndLimit()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        var lines = Enumerable.Range(1, 100).Select(i => $"line {i}");
        await File.WriteAllLinesAsync(file, lines);

        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = file,
            ["offset"] = 50,
            ["limit"] = 5,
        }, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("50\tline 50", result.Output);
        Assert.Contains("54\tline 54", result.Output);
        Assert.DoesNotContain("55\tline 55", result.Output);
    }

    [Fact]
    public async Task FailsOnMissingFile()
    {
        var result = await _tool.ExecuteAsync(new JsonObject
        {
            ["path"] = Path.Combine(_tempDir, "nonexistent.txt"),
        }, CancellationToken.None);

        Assert.True(result.IsError);
    }
}
