using Daisi.Minion.Coding;
using Daisi.Minion.Tui;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Engine;

/// <summary>
/// Executes tool calls from model output. Analyzes calls for safe concurrency:
/// read-only tools run in parallel, writes to the same file are serialized,
/// shell commands always run sequentially.
/// </summary>
public sealed class ToolExecutor
{
    private readonly CodingToolRegistry _registry;
    private readonly AnsiRenderer _renderer;

    // Tools that only read — safe to run concurrently with anything
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
        { "file_read", "grep", "glob", "git" };

    // Tools that must never run concurrently (side effects)
    private static readonly HashSet<string> AlwaysSequentialTools = new(StringComparer.OrdinalIgnoreCase)
        { "shell" };

    public ToolExecutor(CodingToolRegistry registry, AnsiRenderer renderer)
    {
        _registry = registry;
        _renderer = renderer;
    }

    /// <summary>
    /// Execute tool calls with automatic concurrency where safe.
    /// </summary>
    public async Task<List<(string Name, string Output)>> ExecuteToolCallsAsync(List<ToolCall> toolCalls, CancellationToken ct)
    {
        if (toolCalls.Count <= 1)
            return await ExecuteSequentialAsync(toolCalls, ct);

        // Build execution groups: concurrent batches that respect dependencies
        var groups = BuildExecutionGroups(toolCalls);
        var allResults = new List<(string Name, string Output)>();

        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                // Single call — run directly
                allResults.Add(await ExecuteSingleAsync(group[0], ct));
            }
            else
            {
                // Parallel batch
                _renderer.WriteInfo($"Running {group.Count} tools in parallel...");
                var tasks = group.Select(call => ExecuteSingleAsync(call, ct)).ToList();
                var results = await Task.WhenAll(tasks);
                allResults.AddRange(results);
            }
        }

        return allResults;
    }

    private async Task<List<(string Name, string Output)>> ExecuteSequentialAsync(List<ToolCall> toolCalls, CancellationToken ct)
    {
        var results = new List<(string Name, string Output)>();
        foreach (var call in toolCalls)
            results.Add(await ExecuteSingleAsync(call, ct));
        return results;
    }

    private async Task<(string Name, string Output)> ExecuteSingleAsync(ToolCall call, CancellationToken ct)
    {
        _renderer.WriteToolCall(call.Name, call.Arguments.ToJsonString());
        var result = await _registry.ExecuteAsync(call, ct);
        _renderer.WriteToolResult(call.Name, result.Output, result.IsError);
        return (call.Name, result.Output);
    }

    /// <summary>
    /// Group tool calls into batches that can run concurrently.
    /// Rules:
    ///   - Read-only tools can run in parallel with each other
    ///   - Write tools (file_write, file_edit) can run in parallel if targeting different files
    ///   - Shell commands always start a new sequential group
    ///   - A write to file X blocks any subsequent read/write to file X until it completes
    /// </summary>
    private static List<List<ToolCall>> BuildExecutionGroups(List<ToolCall> calls)
    {
        var groups = new List<List<ToolCall>>();
        var currentGroup = new List<ToolCall>();
        var filesBeingWritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var call in calls)
        {
            if (AlwaysSequentialTools.Contains(call.Name))
            {
                // Flush current group, run shell alone
                if (currentGroup.Count > 0)
                {
                    groups.Add(currentGroup);
                    currentGroup = [];
                    filesBeingWritten.Clear();
                }
                groups.Add([call]);
                continue;
            }

            var targetFile = GetTargetFile(call);

            if (ReadOnlyTools.Contains(call.Name))
            {
                // Reads can join current group unless the file is being written
                if (targetFile != null && filesBeingWritten.Contains(targetFile))
                {
                    // Depends on a pending write — start new group
                    groups.Add(currentGroup);
                    currentGroup = [call];
                    filesBeingWritten.Clear();
                }
                else
                {
                    currentGroup.Add(call);
                }
            }
            else
            {
                // Write tool — check for file conflict
                if (targetFile != null && filesBeingWritten.Contains(targetFile))
                {
                    // Same file being written — start new group
                    groups.Add(currentGroup);
                    currentGroup = [call];
                    filesBeingWritten.Clear();
                }
                else
                {
                    currentGroup.Add(call);
                }

                if (targetFile != null)
                    filesBeingWritten.Add(targetFile);
            }
        }

        if (currentGroup.Count > 0)
            groups.Add(currentGroup);

        return groups;
    }

    /// <summary>Extract the target file path from a tool call's arguments, if applicable.</summary>
    private static string? GetTargetFile(ToolCall call)
    {
        if (call.Arguments.TryGetPropertyValue("path", out var pathNode))
        {
            var path = pathNode?.GetValue<string>();
            if (!string.IsNullOrEmpty(path))
                return Path.GetFullPath(path);
        }
        return null;
    }
}
