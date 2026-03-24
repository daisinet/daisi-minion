using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Daisi.Minion.Coding.Tools;

public sealed class GrepTool : IMinionTool
{
    private readonly ToolSandbox? _sandbox;

    public GrepTool(ToolSandbox? sandbox = null) => _sandbox = sandbox;

    public string Name => "grep";
    public string Description => "Search for a regex pattern in files. Returns matching lines with file paths and line numbers. Respects .gitignore.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["pattern"] = new JsonObject { ["type"] = "string", ["description"] = "Regex pattern to search for" },
            ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Directory or file to search in (default: current directory)" },
            ["glob"] = new JsonObject { ["type"] = "string", ["description"] = "File glob filter (e.g. '*.cs', '*.ts')" },
            ["max_results"] = new JsonObject { ["type"] = "integer", ["description"] = "Maximum results to return (default 50)" },
        },
        ["required"] = new JsonArray("pattern"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var pattern = arguments["pattern"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pattern))
            return ToolResult.Error("Missing required parameter: pattern");

        var searchPathArg = arguments["path"]?.GetValue<string>();
        string searchPath;
        try
        {
            searchPath = searchPathArg != null
                ? (_sandbox?.ResolvePath(searchPathArg) ?? Path.GetFullPath(searchPathArg))
                : (_sandbox?.Root ?? Directory.GetCurrentDirectory());
        }
        catch (InvalidOperationException ex) { return ToolResult.Error(ex.Message); }

        var glob = arguments["glob"]?.GetValue<string>() ?? "*";
        var maxResults = ToolArgs.GetInt(arguments, "max_results", 50);

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.Compiled); }
        catch (RegexParseException ex) { return ToolResult.Error($"Invalid regex: {ex.Message}"); }

        var sb = new StringBuilder();
        int count = 0;
        var relativeRoot = _sandbox?.Root ?? Directory.GetCurrentDirectory();

        IEnumerable<string> files;
        if (File.Exists(searchPath))
            files = [searchPath];
        else if (Directory.Exists(searchPath))
            files = Directory.EnumerateFiles(searchPath, glob, SearchOption.AllDirectories)
                .Where(f => !IsIgnored(f));
        else
            return ToolResult.Error($"Path not found: {searchPath}");

        foreach (var file in files)
        {
            if (count >= maxResults) break;
            ct.ThrowIfCancellationRequested();

            string[] lines;
            try { lines = await File.ReadAllLinesAsync(file, ct); }
            catch { continue; }

            for (int i = 0; i < lines.Length && count < maxResults; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    var relPath = Path.GetRelativePath(relativeRoot, file);
                    sb.AppendLine($"{relPath}:{i + 1}: {lines[i]}");
                    count++;
                }
            }
        }

        if (count == 0)
            return ToolResult.Success("No matches found.");

        if (count >= maxResults)
            sb.AppendLine($"... (results limited to {maxResults})");

        return ToolResult.Success(sb.ToString());
    }

    private static bool IsIgnored(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is ".git" or "node_modules" or "bin" or "obj" or ".vs");
    }
}
