namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Word-wrapping calculation and rendering for the command input bar.
/// Line 1 uses "> " prefix, continuation lines use "  " prefix.
/// All methods are pure functions — no terminal I/O.
/// </summary>
public static class CommandBar
{
    private const string Prompt = "> ";
    private const string Continuation = "  ";

    /// <summary>How many terminal rows the input text will occupy at the given width.</summary>
    public static int HeightForText(string text, int width)
    {
        var firstLineMax = width - Prompt.Length;
        if (firstLineMax <= 0) return 1;
        if (string.IsNullOrEmpty(text) || text.Length <= firstLineMax) return 1;

        var remaining = text.Length - firstLineMax;
        var contLineMax = width - Continuation.Length;
        if (contLineMax <= 0) return 1;
        return 1 + (int)Math.Ceiling((double)remaining / contLineMax);
    }

    /// <summary>
    /// Render the input text into display lines, each exactly <paramref name="width"/> chars (space-padded).
    /// </summary>
    public static string[] Render(string text, int width)
    {
        var firstLineMax = width - Prompt.Length;
        if (firstLineMax <= 0) return [Prompt];

        if (string.IsNullOrEmpty(text) || text.Length <= firstLineMax)
            return [Prompt + (text ?? "").PadRight(firstLineMax)];

        var lines = new List<string>();
        lines.Add(Prompt + text[..firstLineMax]);

        var pos = firstLineMax;
        var contLineMax = width - Continuation.Length;
        if (contLineMax <= 0) return [.. lines];

        while (pos < text.Length)
        {
            var len = Math.Min(contLineMax, text.Length - pos);
            lines.Add(Continuation + text.Substring(pos, len).PadRight(contLineMax));
            pos += len;
        }

        return [.. lines];
    }

    /// <summary>
    /// Map a buffer cursor position to (row, col) within the rendered command bar.
    /// Row 0 is the first line. Col is measured from left edge of terminal.
    /// </summary>
    public static (int row, int col) CursorPosition(int cursorPos, int width)
    {
        var firstLineMax = width - Prompt.Length;
        if (firstLineMax <= 0) return (0, Prompt.Length);

        if (cursorPos <= firstLineMax)
            return (0, Prompt.Length + cursorPos);

        var remaining = cursorPos - firstLineMax;
        var contLineMax = width - Continuation.Length;
        if (contLineMax <= 0) return (0, Prompt.Length);

        var row = 1 + remaining / contLineMax;
        var col = Continuation.Length + remaining % contLineMax;
        return (row, col);
    }
}
