using Daisi.Llogos.Chat;

namespace Daisi.Minion.Coding;

/// <summary>
/// Registry of all available coding tools.
/// </summary>
public sealed class CodingToolRegistry
{
    private readonly Dictionary<string, IMinionTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IMinionTool> Tools => _tools;

    public void Register(IMinionTool tool) => _tools[tool.Name] = tool;

    /// <summary>
    /// Get tool definitions suitable for injection into model prompts.
    /// </summary>
    public List<ToolDefinition> GetToolDefinitions()
    {
        var defs = new List<ToolDefinition>();
        foreach (var tool in _tools.Values)
            defs.Add(new ToolDefinition(tool.Name, tool.Description, tool.ParametersSchema));
        return defs;
    }

    /// <summary>
    /// Execute a tool call by name.
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
            return ToolResult.Error($"Unknown tool: {call.Name}");

        try
        {
            return await tool.ExecuteAsync(call.Arguments, ct);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Tool error: {ex.Message}");
        }
    }
}
