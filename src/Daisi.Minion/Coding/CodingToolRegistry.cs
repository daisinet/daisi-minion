using Daisi.Llogos.Chat;

namespace Daisi.Minion.Coding;

/// <summary>
/// Registry of all available coding tools.
/// </summary>
public sealed class CodingToolRegistry
{
    private readonly Dictionary<string, IMinionTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sealedTools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IMinionTool> Tools => _tools;

    /// <summary>
    /// Register a tool. Throws if a sealed tool with the same name already exists.
    /// </summary>
    public void Register(IMinionTool tool)
    {
        if (_sealedTools.Contains(tool.Name))
            throw new InvalidOperationException($"Cannot replace sealed base tool: {tool.Name}");
        _tools[tool.Name] = tool;
    }

    /// <summary>
    /// Seal all currently registered tools. Sealed tools cannot be replaced by future Register calls.
    /// Called after base tools are registered to prevent modules from overriding them.
    /// </summary>
    public void SealBaseTools()
    {
        foreach (var name in _tools.Keys)
            _sealedTools.Add(name);
    }

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
        // Validate tool exists and arguments match schema
        var (tool, validationError) = ToolCallValidator.ValidateCall(call.Name, call.Arguments, _tools);
        if (validationError != null)
        {
            // Try type coercion before rejecting
            if (tool != null)
            {
                var coercions = ToolCallValidator.CoerceTypes(call.Arguments, tool.ParametersSchema);
                if (coercions.Count > 0)
                {
                    // Re-validate after coercion
                    var recheck = ToolCallValidator.Validate(call.Arguments, tool.ParametersSchema);
                    if (recheck == null)
                    {
                        // Coercion fixed it — proceed with execution
                        try { return await tool.ExecuteAsync(call.Arguments, ct); }
                        catch (Exception ex) { return ToolResult.Error($"Tool error: {ex.Message}"); }
                    }
                }
            }
            return ToolResult.Error(validationError);
        }

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
