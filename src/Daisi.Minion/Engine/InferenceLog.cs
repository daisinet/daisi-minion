using System.Diagnostics;
using System.Text;

namespace Daisi.Minion.Engine;

/// <summary>
/// Debug-only inference logger. Maintains a running log of the full chat session
/// including every rendered prompt and raw model output. Resets on /clear, /compact,
/// or model reload. File: ~/.daisi-minion/inference.log
///
/// Only active in Debug builds — compiles to no-ops in Release.
/// </summary>
public static class InferenceLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".daisi-minion", "inference.log");

    /// <summary>Reset the log file (called on /clear, /compact, model reload).</summary>
    [Conditional("DEBUG")]
    public static void Reset(string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath,
                $"═══ Log reset @ {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {reason} ═══\n\n");
        }
        catch { }
    }

    /// <summary>Log the start of an inference request with the full rendered prompt.</summary>
    [Conditional("DEBUG")]
    public static void BeginRequest(string userInput, string renderedPrompt)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"═══ Request @ {DateTime.Now:HH:mm:ss.fff} ═══");
            sb.AppendLine($"[User] {userInput}");
            sb.AppendLine();
            sb.AppendLine("[Full Rendered Prompt]");
            sb.AppendLine(renderedPrompt);
            sb.AppendLine("[Raw Model Output]");
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { }
    }

    /// <summary>Append raw model output tokens as they stream in.</summary>
    [Conditional("DEBUG")]
    public static void AppendToken(string token)
    {
        try { File.AppendAllText(LogPath, token); }
        catch { }
    }

    /// <summary>Mark the end of the response with stats.</summary>
    [Conditional("DEBUG")]
    public static void EndRequest(int tokenCount, string stopReason)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"\n\n[End] {tokenCount} tokens | {stopReason}\n");
        }
        catch { }
    }
}
