using System.Text;
using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding.Tools;

public sealed class GlobTool : IMinionTool
{
    public string Name => "glob";
    public string Description => "Find files matching a glob pattern. Returns matching file paths sorted by modification time.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["pattern"] = new JsonObject { ["type"] = "string", ["description"] = "Glob pattern (e.g. '**/*.cs', 'src/**/*.ts')" },
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Directory to search in (default: current directory)" },
        },
        ["required"] = new JsonArray("pattern"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var pattern = arguments["pattern"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pattern))
            return Task.FromResult(ToolResult.Error("Missing required parameter: pattern"));

        var searchPath = arguments["path"]?.GetValue<string>() ?? Directory.GetCurrentDirectory();
        searchPath = Path.GetFullPath(searchPath);

        if (!Directory.Exists(searchPath))
            return Task.FromResult(ToolResult.Error($"Directory not found: {searchPath}"));

        // Convert glob to search pattern
        var searchPattern = pattern.Replace("**/", "");
        if (string.IsNullOrEmpty(searchPattern)) searchPattern = "*";

        var searchOption = pattern.Contains("**/") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = Directory.EnumerateFiles(searchPath, searchPattern, searchOption)
            .Where(f => !IsIgnored(f))
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .Take(100)
            .ToList();

        if (files.Count == 0)
            return Task.FromResult(ToolResult.Success("No files found."));

        var sb = new StringBuilder();
        foreach (var file in files)
        {
            var relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);
            sb.AppendLine(relPath);
        }

        return Task.FromResult(ToolResult.Success(sb.ToString()));
    }

    private static bool IsIgnored(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is ".git" or "node_modules" or "bin" or "obj" or ".vs");
    }
}
