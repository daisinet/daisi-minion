using System.Text;
using System.Text.RegularExpressions;

namespace Daisi.Minion.Tui;

/// <summary>
/// ANSI terminal renderer for markdown, diffs, code blocks, and tool output.
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

    private Timer? _spinnerTimer;
    private int _spinnerFrame;
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    /// <summary>Write the startup banner.</summary>
    public void WriteBanner()
    {
        Console.WriteLine($"{Bold}{Cyan}daisi-minion{Reset} {Dim}— local AI coding assistant{Reset}");
        Console.WriteLine($"{Dim}Type /help for commands, Ctrl+C to exit{Reset}");
        Console.WriteLine();
    }

    /// <summary>Write the input prompt showing the current directory.</summary>
    public void WritePrompt()
    {
        var cwd = Path.GetFileName(Directory.GetCurrentDirectory());
        Console.Write($"{Bold}{Blue}{cwd}{Reset} {Bold}>{Reset} ");
    }

    /// <summary>Write a streaming token from the model.</summary>
    public void WriteToken(string token)
    {
        Console.Write(token);
    }

    /// <summary>End the current response line.</summary>
    public void EndResponse()
    {
        Console.WriteLine();
        Console.WriteLine();
    }

    /// <summary>Write a tool call intent.</summary>
    public void WriteToolCall(string toolName, string argsJson)
    {
        Console.WriteLine();
        Console.Write($"  {Yellow}⚡ {toolName}{Reset}");
        if (argsJson.Length < 100)
            Console.Write($" {Dim}{argsJson}{Reset}");
        Console.WriteLine();
    }

    /// <summary>Write a tool result.</summary>
    public void WriteToolResult(string toolName, string output, bool isError)
    {
        var color = isError ? Red : Green;
        var icon = isError ? "✗" : "✓";
        Console.WriteLine($"  {color}{icon} {toolName}{Reset}");

        if (!string.IsNullOrEmpty(output))
        {
            var lines = output.Split('\n');
            var displayLines = lines.Length > 20 ? lines.Take(20).Append($"... ({lines.Length - 20} more lines)") : lines;
            foreach (var line in displayLines)
                Console.WriteLine($"  {Dim}│{Reset} {line}");
        }
        Console.WriteLine();
    }

    /// <summary>Write an error message.</summary>
    public void WriteError(string message)
    {
        Console.WriteLine($"{Red}Error: {message}{Reset}");
    }

    /// <summary>Write an info message.</summary>
    public void WriteInfo(string message)
    {
        Console.WriteLine($"{Dim}{message}{Reset}");
    }

    /// <summary>Write a success message.</summary>
    public void WriteSuccess(string message)
    {
        Console.WriteLine($"{Green}{message}{Reset}");
    }

    /// <summary>Start a spinner with a message.</summary>
    public void StartSpinner(string message)
    {
        _spinnerFrame = 0;
        _spinnerTimer = new Timer(_ =>
        {
            var frame = SpinnerFrames[_spinnerFrame % SpinnerFrames.Length];
            Console.Write($"\r  {Cyan}{frame}{Reset} {Dim}{message}{Reset}  ");
            _spinnerFrame++;
        }, null, 0, 80);
    }

    /// <summary>Stop the spinner.</summary>
    public void StopSpinner()
    {
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
        Console.Write("\r\x1b[2K"); // Clear line
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
