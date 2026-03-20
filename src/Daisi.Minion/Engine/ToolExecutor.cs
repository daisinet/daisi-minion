using Daisi.Minion.Coding;
using Daisi.Minion.Tui;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Executes tool calls from model output and formats results.
/// </summary>
public sealed class ToolExecutor
{
    private readonly CodingToolRegistry _registry;
    private readonly AnsiRenderer _renderer;

    public ToolExecutor(CodingToolRegistry registry, AnsiRenderer renderer)
    {
        _registry = registry;
        _renderer = renderer;
    }

    /// <summary>
    /// Execute all tool calls in the response and return formatted results for the model.
    /// </summary>
    public async Task<List<string>> ExecuteToolCallsAsync(List<ToolCall> toolCalls, CancellationToken ct)
    {
        var results = new List<string>();

        foreach (var call in toolCalls)
        {
            _renderer.WriteToolCall(call.Name, call.Arguments.ToJsonString());

            var result = await _registry.ExecuteAsync(call, ct);

            _renderer.WriteToolResult(call.Name, result.Output, result.IsError);

            results.Add(result.Output);
        }

        return results;
    }
}
