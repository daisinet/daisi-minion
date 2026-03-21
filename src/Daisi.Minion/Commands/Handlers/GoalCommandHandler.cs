using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class GoalCommandHandler(
    AnsiRenderer renderer,
    Func<string, int, Task> onGoalSet) : ISlashCommandHandler
{
    public async Task HandleAsync(string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            renderer.WriteInfoHeader("Autonomous goal mode");
            renderer.WriteInfo("The minion will work toward a goal using tools, iterating");
            renderer.WriteInfo("until it declares the goal complete or you press Esc Esc.");
            renderer.WriteInfo("");
            renderer.WriteInfo("Usage: /goal <description>");
            renderer.WriteInfo("       /goal Fix the null reference in UserService.cs");
            renderer.WriteInfo("       /goal Refactor the auth module to use dependency injection");
            renderer.WriteInfo("");
            renderer.WriteInfo("Options:");
            renderer.WriteInfo("  /goal <description> --max=N   Limit to N iterations (default: 20)");
            return;
        }

        // Parse --max=N option
        var maxIterations = 20;
        var goalText = args;
        var maxIdx = args.IndexOf("--max=", StringComparison.OrdinalIgnoreCase);
        if (maxIdx >= 0)
        {
            var afterMax = args[(maxIdx + 6)..].Trim().Split(' ')[0];
            if (int.TryParse(afterMax, out var parsed) && parsed > 0)
                maxIterations = parsed;
            goalText = args[..maxIdx].Trim();
        }

        if (string.IsNullOrWhiteSpace(goalText))
        {
            renderer.WriteError("Please provide a goal description.");
            return;
        }

        await onGoalSet(goalText, maxIterations);
    }
}
