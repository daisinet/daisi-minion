namespace Daisi.Minion.Tui;

/// <summary>
/// Handles streaming display of model output with markdown awareness.
/// Buffers tokens to detect code blocks and special formatting.
/// </summary>
public sealed class StreamingDisplay
{
    private readonly AnsiRenderer _renderer;
    private readonly System.Text.StringBuilder _fullResponse = new();
    private bool _inCodeBlock;

    // ANSI codes
    private const string Reset = "\x1b[0m";
    private const string BgGray = "\x1b[48;5;236m";

    public StreamingDisplay(AnsiRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// Process and display a streaming token.
    /// </summary>
    public void WriteToken(string token)
    {
        _fullResponse.Append(token);

        // Check for code block boundaries
        var text = _fullResponse.ToString();
        var backtickCount = CountSubstring(text, "```");
        var shouldBeInCodeBlock = backtickCount % 2 == 1;

        if (shouldBeInCodeBlock != _inCodeBlock)
        {
            _inCodeBlock = shouldBeInCodeBlock;
            if (_inCodeBlock)
                Console.Write(BgGray);
            else
                Console.Write(Reset);
        }

        Console.Write(token);
    }

    /// <summary>
    /// Finish the current response stream.
    /// </summary>
    public string Finish()
    {
        if (_inCodeBlock)
        {
            Console.Write(Reset);
            _inCodeBlock = false;
        }
        Console.WriteLine();
        Console.WriteLine();

        var result = _fullResponse.ToString();
        _fullResponse.Clear();
        return result;
    }

    /// <summary>
    /// Get the full response accumulated so far.
    /// </summary>
    public string GetCurrentResponse() => _fullResponse.ToString();

    private static int CountSubstring(string text, string sub)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(sub, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += sub.Length;
        }
        return count;
    }
}
