using Daisi.Minion.Config;
using Daisi.Minion.Tui;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Commands.Handlers;

public sealed class AttentionCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager,
    Action onSettingsChanged) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        var modelPath = configManager.Config.ActiveModel;
        if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
        {
            renderer.WriteError("No model loaded.");
            return Task.CompletedTask;
        }

        var profile = ModelProfile.Load(modelPath) ?? new ModelProfile
        {
            ModelId = Path.GetFileNameWithoutExtension(modelPath)
        };

        if (string.IsNullOrWhiteSpace(args))
        {
            ShowCurrent(profile);
            return Task.CompletedTask;
        }

        var value = args.Trim();

        // Validate by parsing
        try
        {
            var strategy = AttentionStrategy.Parse(value);
            // Format back to canonical form for display
            var display = strategy.Mode switch
            {
                AttentionMode.Full => "full",
                AttentionMode.Window => $"window:{strategy.WindowSize}",
                AttentionMode.Sinks => $"sinks:{strategy.SinkTokens},{strategy.WindowSize}",
                _ => value
            };

            profile.Attention = display;
            profile.Save(modelPath);

            renderer.WriteSuccess($"Attention strategy set to: {display}");
            renderer.WriteInfo("Reloading model...");
            onSettingsChanged();
        }
        catch (FormatException ex)
        {
            renderer.WriteError(ex.Message);
        }

        return Task.CompletedTask;
    }

    private void ShowCurrent(ModelProfile profile)
    {
        renderer.WriteInfoHeader($"Attention strategy: {profile.Attention}");
        renderer.WriteInfo("");
        renderer.WriteInfo("Strategies:");
        renderer.WriteInfo("  full              Full attention over entire context (default)");
        renderer.WriteInfo("  window:<N>        Sliding window of N tokens (oldest evicted)");
        renderer.WriteInfo("  sinks:<S>,<W>     Sliding window with S protected initial tokens");
        renderer.WriteInfo("");
        renderer.WriteInfo("Usage: /attention full");
        renderer.WriteInfo("       /attention window:2048");
        renderer.WriteInfo("       /attention sinks:4,2048");
    }
}
