using Daisi.Minion.Config;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class InfSettingsCommandHandler(
    AnsiRenderer renderer,
    ConfigManager configManager) : ISlashCommandHandler
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = ["temperature", "temp"],
        ["top_k"] = ["top_k", "topk"],
        ["top_p"] = ["top_p", "topp"],
        ["repetition_penalty"] = ["repetition_penalty", "rep_pen", "rep"],
        ["max_tokens"] = ["max_tokens", "max"],
        ["context_size"] = ["context_size", "ctx"],
    };

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

        // Parse key=value pairs
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var changed = new List<string>();

        foreach (var part in parts)
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx <= 0)
            {
                renderer.WriteError($"Invalid format: {part} (expected key=value)");
                return Task.CompletedTask;
            }

            var key = part[..eqIdx].Trim();
            var val = part[(eqIdx + 1)..].Trim();

            if (!TryApply(profile, key, val, out var error))
            {
                renderer.WriteError(error);
                return Task.CompletedTask;
            }

            changed.Add($"{key}={val}");
        }

        profile.Save(modelPath);

        renderer.WriteSuccess($"Updated: {string.Join(", ", changed)}");
        ShowCurrent(profile);
        return Task.CompletedTask;
    }

    private void ShowCurrent(ModelProfile profile)
    {
        renderer.WriteInfo("Current inference settings:");
        renderer.WriteInfo($"  temp={profile.Temperature}  top_k={profile.TopK}  top_p={profile.TopP}");
        renderer.WriteInfo($"  rep_pen={profile.RepetitionPenalty}  max={profile.MaxTokens}  ctx={profile.ContextSize}");
        renderer.WriteInfo("");
        renderer.WriteInfo("Usage: /inf-settings key=value [key=value ...]");
        renderer.WriteInfo("Keys: temp, top_k, top_p, rep_pen, max, ctx");
    }

    private static bool TryApply(ModelProfile profile, string key, string value, out string error)
    {
        var field = ResolveField(key);

        switch (field)
        {
            case "temperature":
                if (!float.TryParse(value, out var temp) || temp < 0) { error = $"Invalid temperature: {value}"; return false; }
                profile.Temperature = temp;
                break;
            case "top_k":
                if (!int.TryParse(value, out var topK) || topK < 0) { error = $"Invalid top_k: {value}"; return false; }
                profile.TopK = topK;
                break;
            case "top_p":
                if (!float.TryParse(value, out var topP) || topP < 0 || topP > 1) { error = $"Invalid top_p: {value} (0.0-1.0)"; return false; }
                profile.TopP = topP;
                break;
            case "repetition_penalty":
                if (!float.TryParse(value, out var rep) || rep < 0) { error = $"Invalid repetition_penalty: {value}"; return false; }
                profile.RepetitionPenalty = rep;
                break;
            case "max_tokens":
                if (!int.TryParse(value, out var max) || max <= 0) { error = $"Invalid max_tokens: {value}"; return false; }
                profile.MaxTokens = max;
                break;
            case "context_size":
                if (!int.TryParse(value, out var ctx) || ctx <= 0) { error = $"Invalid context_size: {value}"; return false; }
                profile.ContextSize = ctx;
                break;
            default:
                error = $"Unknown setting: {key}. Valid: temp, top_k, top_p, rep_pen, max, ctx";
                return false;
        }

        error = "";
        return true;
    }

    private static string? ResolveField(string key)
    {
        foreach (var (field, aliases) in Aliases)
        {
            if (aliases.Any(a => a.Equals(key, StringComparison.OrdinalIgnoreCase)))
                return field;
        }
        return null;
    }
}
