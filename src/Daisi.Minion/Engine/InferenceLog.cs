using System.Diagnostics;
using System.Text;

namespace Daisi.Minion.Engine;

/// <summary>
/// Debug-only inference logger. Writes the rendered prompt and raw model output
/// to a log file for each inference request. File is reset per request.
/// Only active in Debug builds — compiles to no-ops in Release.
/// </summary>
public static class InferenceLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".daisi-minion", "inference.log");

    /// <summary>Write the rendered prompt at the start of an inference request.</summary>
    [Conditional("DEBUG")]
    public static void BeginRequest(string userInput, string renderedPrompt)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder();
            sb.AppendLine($"═══ Inference Request @ {DateTime.Now:HH:mm:ss.fff} ═══");
            sb.AppendLine();
            sb.AppendLine($"[User Input] {userInput}");
            sb.AppendLine();
            sb.AppendLine("[Rendered Prompt]");
            sb.AppendLine(renderedPrompt);
            sb.AppendLine();
            sb.AppendLine("[Raw Model Output]");
            File.WriteAllText(LogPath, sb.ToString());
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

    /// <summary>Mark the end of the response.</summary>
    [Conditional("DEBUG")]
    public static void EndRequest(int tokenCount, string stopReason)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"[End] {tokenCount} tokens, stopped by: {stopReason}");
            sb.AppendLine($"═══ End ═══");
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { }
    }
}
