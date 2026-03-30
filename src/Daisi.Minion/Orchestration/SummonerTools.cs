using System.Text;
using System.Text.Json.Nodes;
using Daisi.Minion.Coding;
using Daisi.Minion.Engine;

namespace Daisi.Minion.Orchestration;

/// <summary>Spawn a new child minion with a specific type and task.</summary>
public sealed class SpawnMinionTool : IMinionTool
{
    private readonly MinionPool _pool;

    public SpawnMinionTool(MinionPool pool) => _pool = pool;

    public string Name => "spawn_minion";
    public string Description => "Spawn a new worker minion. Types: code, test, research. Always include acceptance_criteria so the minion knows what 'done well' looks like.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["minion_type"] = new JsonObject { ["type"] = "string", ["description"] = "Type of minion: code, test, research" },
            ["task"] = new JsonObject { ["type"] = "string", ["description"] = "Clear, specific task description for the minion" },
            ["acceptance_criteria"] = new JsonObject { ["type"] = "string", ["description"] = "Checklist of criteria the minion must meet. One per line. e.g.:\n- Code compiles without errors\n- Unit test exists and passes\n- No hardcoded paths" },
        },
        ["required"] = new JsonArray("minion_type", "task"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var typeName = ToolArgs.GetString(arguments, "minion_type");
        var task = ToolArgs.GetString(arguments, "task");
        var criteria = ToolArgs.GetString(arguments, "acceptance_criteria");

        if (string.IsNullOrEmpty(typeName))
            return Task.FromResult(ToolResult.Error("Missing required parameter: minion_type"));
        if (string.IsNullOrEmpty(task))
            return Task.FromResult(ToolResult.Error("Missing required parameter: task"));

        try
        {
            var id = _pool.Spawn(typeName, task, criteria);
            return Task.FromResult(ToolResult.Success($"Spawned {id}. Task: {task}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to spawn: {ex.Message}"));
        }
    }
}

/// <summary>Check the status and last output of a child minion.</summary>
public sealed class CheckMinionTool : IMinionTool
{
    private readonly MinionPool _pool;

    public CheckMinionTool(MinionPool pool) => _pool = pool;

    public string Name => "check_minion";
    public string Description => "Check the status of a spawned minion. Returns its status and last response.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["minion_id"] = new JsonObject { ["type"] = "string", ["description"] = "ID of the minion to check (e.g. 'code-1')" },
        },
        ["required"] = new JsonArray("minion_id"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var id = ToolArgs.GetString(arguments, "minion_id");
        if (string.IsNullOrEmpty(id))
            return Task.FromResult(ToolResult.Error("Missing required parameter: minion_id"));

        if (!_pool.Children.TryGetValue(id, out var child))
            return Task.FromResult(ToolResult.Error($"Unknown minion: {id}"));

        var sb = new StringBuilder();
        sb.AppendLine($"Minion: {child.Id}");
        sb.AppendLine($"Type: {child.TypeName}");
        sb.AppendLine($"Status: {child.Status}");
        sb.AppendLine($"Task: {child.Task}");
        if (child.AcceptanceCriteria != null)
            sb.AppendLine($"Acceptance criteria:\n{child.AcceptanceCriteria}");
        sb.AppendLine($"Iterations: {child.IterationCount}");
        sb.AppendLine($"Tool calls: {child.ToolCallCount}");
        sb.AppendLine($"Files modified: {child.FilesModified.Count}");
        sb.AppendLine($"Duration: {child.Stopwatch.Elapsed.TotalSeconds:F1}s");

        // Show file content previews for completed minions so summoner can verify quality
        if (child.Status == ChildMinionStatus.Complete && child.FilesModified.Count > 0)
        {
            sb.AppendLine("\n--- Files Created ---");
            foreach (var filePath in child.FilesModified.Distinct().Take(5))
            {
                if (!File.Exists(filePath)) continue;
                try
                {
                    var content = File.ReadAllText(filePath);
                    var preview = content.Length > 800 ? content[..800] + "\n... (truncated)" : content;
                    sb.AppendLine($"\n=== {Path.GetFileName(filePath)} ({content.Length} bytes) ===");
                    sb.AppendLine(preview);
                }
                catch { }
            }
        }

        if (child.LastResponse != null)
        {
            var response = child.LastResponse.Length > 500
                ? child.LastResponse[..500] + "..."
                : child.LastResponse;
            sb.AppendLine($"\nLast response: {response}");
        }

        return Task.FromResult(ToolResult.Success(sb.ToString()));
    }
}

/// <summary>Send a message/task to a child minion and get its response.</summary>
public sealed class SendMessageTool : IMinionTool
{
    private readonly MinionPool _pool;

    public SendMessageTool(MinionPool pool) => _pool = pool;

    public string Name => "send_message";
    public string Description => "Send a message or task to a spawned minion. The minion will work on it and return a response.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["minion_id"] = new JsonObject { ["type"] = "string", ["description"] = "ID of the target minion" },
            ["message"] = new JsonObject { ["type"] = "string", ["description"] = "Message or task to send" },
        },
        ["required"] = new JsonArray("minion_id", "message"),
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var id = ToolArgs.GetString(arguments, "minion_id");
        var message = ToolArgs.GetString(arguments, "message");

        if (string.IsNullOrEmpty(id))
            return ToolResult.Error("Missing required parameter: minion_id");
        if (string.IsNullOrEmpty(message))
            return ToolResult.Error("Missing required parameter: message");

        try
        {
            var response = await _pool.SendAsync(id, message, ct);

            // Strip any <tool_call> blocks from child response — the summoner should not
            // try to execute the child's tool calls. They were already handled by SendAsync.
            var cleanResponse = QwenToolCallParser.GetTextBeforeToolCalls(response);
            if (string.IsNullOrWhiteSpace(cleanResponse))
                cleanResponse = response.Contains("TASK_COMPLETE") ? "TASK_COMPLETE" : "(minion produced tool calls only, no text response)";

            var truncated = cleanResponse.Length > 2000 ? cleanResponse[..2000] + "..." : cleanResponse;
            return ToolResult.Success($"[{id}] {truncated}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to send to {id}: {ex.Message}");
        }
    }
}

/// <summary>Stop a child minion and free its resources.</summary>
public sealed class StopMinionTool : IMinionTool
{
    private readonly MinionPool _pool;

    public StopMinionTool(MinionPool pool) => _pool = pool;

    public string Name => "stop_minion";
    public string Description => "Stop a spawned minion and free its resources.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["minion_id"] = new JsonObject { ["type"] = "string", ["description"] = "ID of the minion to stop" },
        },
        ["required"] = new JsonArray("minion_id"),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        var id = ToolArgs.GetString(arguments, "minion_id");
        if (string.IsNullOrEmpty(id))
            return Task.FromResult(ToolResult.Error("Missing required parameter: minion_id"));

        _pool.Stop(id);
        return Task.FromResult(ToolResult.Success($"Stopped {id}."));
    }
}

/// <summary>List all active child minions with their status.</summary>
public sealed class ListMinionsTool : IMinionTool
{
    private readonly MinionPool _pool;

    public ListMinionsTool(MinionPool pool) => _pool = pool;

    public string Name => "list_minions";
    public string Description => "List all spawned minions with their current status.";

    public JsonObject ParametersSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
    };

    public Task<ToolResult> ExecuteAsync(JsonObject arguments, CancellationToken ct)
    {
        return Task.FromResult(ToolResult.Success(_pool.GetPoolSummary()));
    }
}
