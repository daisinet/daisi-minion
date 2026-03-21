using System.Text;
using System.Text.RegularExpressions;
using Daisi.Minion.Tui.Layout;

namespace Daisi.Minion.Tui;

/// <summary>
/// ANSI terminal renderer for markdown, diffs, code blocks, and tool output.
/// All output is routed through ConsoleOutput for thread safety.
/// </summary>
public sealed partial class AnsiRenderer
{
    // ANSI color codes
    private const string Reset = "\x1b[0m";
    private const string Bold = "\x1b[1m";
    private const string Dim = "\x1b[2m";
    private const string Italic = "\x1b[3m";
    private const string Green = "\x1b[32m";
    private const string Red = "\x1b[31m";
    private const string Yellow = "\x1b[33m";
    private const string Blue = "\x1b[34m";
    private const string Cyan = "\x1b[36m";
    private const string Gray = "\x1b[90m";
    private const string BgGray = "\x1b[48;5;236m";
    private const string BgDarkGray = "\x1b[48;5;235m";
    private const string BgMutedRed = "\x1b[48;5;52m";
    private const string White = "\x1b[97m";

    private ConsoleOutput? _output;

    /// <summary>Set the ConsoleOutput to route all writes through. If null, writes go directly to Console.</summary>
    public void SetOutput(ConsoleOutput output) => _output = output;

    internal void Write(string text)
    {
        if (_output != null)
            _output.WriteContent(text);
        else
            Console.Write(text);
    }

    internal void WriteLine(string text = "")
    {
        if (_output != null)
            _output.WriteContentLine(text);
        else
            Console.WriteLine(text);
    }

    /// <summary>Write the startup banner.</summary>
    public void WriteBanner(string name = "minion")
    {
        WriteLine($"{Bold}{Cyan}{name}{Reset} {Dim}— local AI assistant{Reset}");
        WriteLine($"{Dim}Type /help for commands, Shift+Tab to cycle personas, Ctrl+C to exit{Reset}");
        WriteLine();
    }

    /// <summary>Echo the user's input into the content area with a muted background.</summary>
    public void WriteUserInput(string text)
    {
        WriteLine($"{BgDarkGray}{White} ▶ {text} {Reset}");
    }

    /// <summary>Write a complete model response, rendered with markdown formatting.</summary>
    public void WriteResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        WriteLine();
        Write(RenderMarkdown(text));
        WriteLine();
    }

    /// <summary>
    /// Create a line-at-a-time markdown writer that tracks code block state
    /// across calls. Each call to WriteLine renders and outputs one line.
    /// </summary>
    public MarkdownLineWriter CreateLineWriter()
    {
        return new MarkdownLineWriter(this);
    }

    /// <summary>
    /// Streams markdown output one line at a time, tracking code block state.
    /// </summary>
    public sealed class MarkdownLineWriter
    {
        private readonly AnsiRenderer _renderer;
        private bool _inCodeBlock;
        private bool _inThinkBlock;

        internal MarkdownLineWriter(AnsiRenderer renderer)
        {
            _renderer = renderer;
        }

        /// <summary>Render and output a single line with markdown formatting.</summary>
        public void WriteLine(string line)
        {
            // Handle think tags — can appear inline or on their own line
            var remaining = line;
            while (remaining.Length > 0)
            {
                if (_inThinkBlock)
                {
                    var closeIdx = remaining.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
                    if (closeIdx >= 0)
                    {
                        // Output thinking content up to the close tag
                        var thinkContent = remaining[..closeIdx];
                        if (thinkContent.Length > 0)
                            _renderer.WriteLine($"{Dim}{Italic}  💭 {thinkContent}{Reset}");
                        _inThinkBlock = false;
                        remaining = remaining[(closeIdx + "</think>".Length)..];
                        continue;
                    }
                    else
                    {
                        // Entire remaining line is thinking
                        _renderer.WriteLine($"{Dim}{Italic}  💭 {remaining}{Reset}");
                        return;
                    }
                }

                var openIdx = remaining.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                if (openIdx >= 0)
                {
                    // Output any content before the think tag normally
                    var before = remaining[..openIdx];
                    if (before.Length > 0)
                        WriteFormattedLine(before);
                    _inThinkBlock = true;
                    remaining = remaining[(openIdx + "<think>".Length)..];
                    continue;
                }

                // No think tags — render normally
                WriteFormattedLine(remaining);
                return;
            }
        }

        private void WriteFormattedLine(string line)
        {
            if (line.StartsWith("```"))
            {
                _inCodeBlock = !_inCodeBlock;
                _renderer.WriteLine(_inCodeBlock ? $"{BgGray}" : Reset);
                return;
            }

            if (_inCodeBlock)
            {
                _renderer.WriteLine($"  {line}");
                return;
            }

            // Headers
            if (line.StartsWith("### "))
                _renderer.WriteLine($"{Bold}{line[4..]}{Reset}");
            else if (line.StartsWith("## "))
                _renderer.WriteLine($"{Bold}{Cyan}{line[3..]}{Reset}");
            else if (line.StartsWith("# "))
                _renderer.WriteLine($"{Bold}{Blue}{line[2..]}{Reset}");
            // Diff lines
            else if (line.StartsWith('+') && !line.StartsWith("+++"))
                _renderer.WriteLine($"{Green}{line}{Reset}");
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                _renderer.WriteLine($"{Red}{line}{Reset}");
            else
                _renderer.WriteLine(RenderInlineMarkdown(line));
        }

        /// <summary>Close any open code block formatting.</summary>
        public void Finish()
        {
            if (_inCodeBlock)
            {
                _renderer.Write(Reset);
                _inCodeBlock = false;
            }
            _inThinkBlock = false;
        }
    }

    /// <summary>Write a tool call intent.</summary>
    public void WriteToolCall(string toolName, string argsJson)
    {
        WriteLine();
        var line = $"  {Yellow}⚡ {toolName}{Reset}";
        if (argsJson.Length < 100)
            line += $" {Dim}{argsJson}{Reset}";
        WriteLine(line);
    }

    /// <summary>Write a tool result.</summary>
    public void WriteToolResult(string toolName, string output, bool isError)
    {
        var color = isError ? Red : Green;
        var icon = isError ? "✗" : "✓";
        WriteLine($"  {color}{icon} {toolName}{Reset}");

        if (!string.IsNullOrEmpty(output))
        {
            var lines = output.Split('\n');
            var displayLines = lines.Length > 20 ? lines.Take(20).Append($"... ({lines.Length - 20} more lines)") : lines;
            foreach (var line in displayLines)
                WriteLine($"  {Dim}│{Reset} {line}");
        }
        WriteLine();
    }

    /// <summary>Write an error message with a muted red background.</summary>
    public void WriteError(string message)
    {
        WriteLine($"{BgMutedRed}{White} ✗ {message} {Reset}");
    }

    /// <summary>Write an info message (continuation line, no icon).</summary>
    public void WriteInfo(string message)
    {
        WriteLine($"{Gray}    {message}{Reset}");
    }

    /// <summary>Write a success message with icon.</summary>
    public void WriteSuccess(string message)
    {
        WriteLine($"{Green}  ✓ {message}{Reset}");
    }

    /// <summary>Write an info message with a leading icon (use for the first line of a block).</summary>
    public void WriteInfoHeader(string message)
    {
        WriteLine($"{Gray}  ● {message}{Reset}");
    }

    /// <summary>Render markdown-formatted text with ANSI styling.</summary>
    public string RenderMarkdown(string text)
    {
        var sb = new StringBuilder();
        var inCodeBlock = false;

        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                sb.AppendLine(inCodeBlock ? $"{BgGray}" : Reset);
                continue;
            }

            if (inCodeBlock)
            {
                sb.AppendLine($"  {line}");
                continue;
            }

            // Headers
            if (line.StartsWith("### "))
                sb.AppendLine($"{Bold}{line[4..]}{Reset}");
            else if (line.StartsWith("## "))
                sb.AppendLine($"{Bold}{Cyan}{line[3..]}{Reset}");
            else if (line.StartsWith("# "))
                sb.AppendLine($"{Bold}{Blue}{line[2..]}{Reset}");
            // Diff lines
            else if (line.StartsWith('+') && !line.StartsWith("+++"))
                sb.AppendLine($"{Green}{line}{Reset}");
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                sb.AppendLine($"{Red}{line}{Reset}");
            else
                sb.AppendLine(RenderInlineMarkdown(line));
        }

        return sb.ToString();
    }

    private static string RenderInlineMarkdown(string line)
    {
        // Bold
        line = BoldRegex().Replace(line, $"{Bold}$1{Reset}");
        // Italic
        line = ItalicRegex().Replace(line, $"{Italic}$1{Reset}");
        // Inline code
        line = InlineCodeRegex().Replace(line, $"{BgGray}$1{Reset}");
        return line;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\*(.+?)\*")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"`(.+?)`")]
    private static partial Regex InlineCodeRegex();
}
