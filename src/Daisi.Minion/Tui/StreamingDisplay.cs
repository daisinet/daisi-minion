using Daisi.Minion.Tui.Layout;

namespace Daisi.Minion.Tui;

/// <summary>
/// Handles streaming display of model output with markdown awareness.
/// Buffers tokens to detect code blocks and special formatting.
/// Routes output through ConsoleOutput for thread safety.
/// </summary>
public sealed class StreamingDisplay
{
    private readonly AnsiRenderer _renderer;
    private readonly ConsoleOutput? _output;
    private readonly System.Text.StringBuilder _fullResponse = new();
    private bool _inCodeBlock;

    // ANSI codes
    private const string Reset = "\x1b[0m";
    private const string BgGray = "\x1b[48;5;236m";

    public StreamingDisplay(AnsiRenderer renderer, ConsoleOutput? output = null)
    {
        _renderer = renderer;
        _output = output;
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
            Write(_inCodeBlock ? BgGray : Reset);
        }

        Write(token);
    }

    /// <summary>
    /// Finish the current response stream.
    /// </summary>
    public string Finish()
    {
        if (_inCodeBlock)
        {
            Write(Reset);
            _inCodeBlock = false;
        }
        WriteLine();
        WriteLine();

        var result = _fullResponse.ToString();
        _fullResponse.Clear();
        return result;
    }

    /// <summary>
    /// Get the full response accumulated so far.
    /// </summary>
    public string GetCurrentResponse() => _fullResponse.ToString();

    private void Write(string text)
    {
        if (_output != null)
            _output.WriteContent(text);
        else
            Console.Write(text);
    }

    private void WriteLine(string text = "")
    {
        if (_output != null)
            _output.WriteContentLine(text);
        else
            Console.WriteLine(text);
    }

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
