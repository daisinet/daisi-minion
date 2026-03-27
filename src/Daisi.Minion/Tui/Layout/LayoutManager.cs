using System.Runtime.InteropServices;
using System.Text;

namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Central layout coordinator using ANSI scroll regions (DECSTBM).
/// All writes are batched into a single Console.Write to eliminate flicker.
/// </summary>
public sealed class LayoutManager : IDisposable
{
    private const string Esc = "\x1b[";
    private const string Reset = "\x1b[0m";
    private const string Orange = "\x1b[38;5;208m";
    private const string OrangeBg = "\x1b[48;5;208m";
    private const string Black = "\x1b[30m";
    private const string BlackBg = "\x1b[40m";
    private const string DimWhite = "\x1b[2;37m";
    private const string HideCursor = "\x1b[?25l";
    private const string ShowCursor = "\x1b[?25h";

    private int _termWidth;
    private int _termHeight;
    private int _commandBarHeight = 1;
    private int _lastCursorRow;
    private int _lastCursorCol;
    private string _personaLabel = "";
    private string _roleLabel = "";
    private Timer? _resizeTimer;

    public StatusBar StatusBar { get; } = new();

    // Layout row calculations (1-based for ANSI), bottom-up
    private int ReservedBottom => _commandBarHeight + 5;
    private int ScrollRegionEnd => _termHeight - ReservedBottom;
    private int PadAboveRow => ScrollRegionEnd + 1;
    private int StatusBarRow => ScrollRegionEnd + 2;
    private int PadBelowRow => ScrollRegionEnd + 3;
    private int OrangeTopRow => ScrollRegionEnd + 4;
    private int CommandBarFirstRow => ScrollRegionEnd + 5;
    private int OrangeBottomRow => CommandBarFirstRow + _commandBarHeight;

    /// <summary>Current terminal width.</summary>
    public int Width => _termWidth;

    /// <summary>Set the persona (personality trait) label.</summary>
    public void SetPersonaLabel(string? name)
    {
        _personaLabel = string.IsNullOrEmpty(name) ? "" : $" {name} ";
    }

    /// <summary>Set the role label.</summary>
    public void SetRoleLabel(string name)
    {
        _roleLabel = string.IsNullOrEmpty(name) ? "" : $" {name} ";
    }

    // --- Windows VT100 support ---

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint handle, uint mode);

    private static void EnableWindowsVt100()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = GetStdHandle(-11);
        if (GetConsoleMode(handle, out var mode))
            SetConsoleMode(handle, mode | 0x0004);
    }

    /// <summary>Initialize the layout.</summary>
    public void Initialize()
    {
        EnableWindowsVt100();
        Console.OutputEncoding = Encoding.UTF8;

        _termWidth = Console.WindowWidth;
        _termHeight = Console.WindowHeight;

        SetScrollRegion();
        ClearContent();
        UpdateStatusBar();
        RedrawCommandBar("", 0);

        _resizeTimer = new Timer(_ =>
        {
            // Check without lock first (cheap) to avoid lock contention
            if (Console.WindowWidth == _termWidth && Console.WindowHeight == _termHeight) return;
            CheckResize();
        }, null, 250, 250);
    }

    /// <summary>Write text into the content scroll region.</summary>
    public void WriteToContent(string text)
    {
        var buf = new StringBuilder();
        buf.Append(HideCursor);
        // Move to bottom of scroll region — text with \n will auto-scroll
        buf.Append($"{Esc}{ScrollRegionEnd};1H");
        buf.Append(text);
        // Return cursor to command bar
        AppendCursorRestore(buf);
        Flush(buf);
    }

    /// <summary>Redraw the status bar at its fixed row.</summary>
    public void UpdateStatusBar()
    {
        var content = StatusBar.Render(_termWidth);
        var buf = new StringBuilder();
        buf.Append(HideCursor);

        buf.Append($"{Esc}{PadAboveRow};1H{Esc}2K");

        buf.Append($"{Esc}{StatusBarRow};1H{Esc}2K");
        buf.Append($"{BlackBg}{DimWhite}");
        if (content.Length >= _termWidth)
            buf.Append(content.AsSpan(0, _termWidth));
        else
            buf.Append(content).Append(' ', _termWidth - content.Length);
        buf.Append(Reset);

        buf.Append($"{Esc}{PadBelowRow};1H{Esc}2K");

        AppendCursorRestore(buf);
        Flush(buf);
    }

    /// <summary>Update only the spinner character. Single atomic write.</summary>
    public void UpdateSpinnerChar()
    {
        var ch = StatusBar.CurrentSpinnerChar;
        if (ch == null) return;

        // Single write: hide cursor, move to spinner pos, write char, restore
        var buf = new StringBuilder();
        buf.Append(HideCursor);
        buf.Append($"{Esc}{StatusBarRow};{StatusBar.SpinnerCol}H");
        buf.Append($"{BlackBg}{DimWhite}{ch}{Reset}");
        AppendCursorRestore(buf);
        Flush(buf);
    }

    /// <summary>
    /// Rewrite the spinner area (char + message) on the status bar without
    /// touching the right side indicators. Avoids full-line clear flicker.
    /// </summary>
    public void UpdateSpinnerMessage()
    {
        if (!StatusBar.IsSpinning) return;

        var ch = StatusBar.CurrentSpinnerChar ?? " ";
        var msg = StatusBar.CurrentSpinnerMessage ?? "";

        // Calculate max width for the left side (leave room for right indicators)
        var maxLeft = _termWidth / 2;
        if (msg.Length + 4 > maxLeft)
            msg = msg[..(maxLeft - 4)];

        var buf = new StringBuilder();
        buf.Append(HideCursor);
        // Move to start of status bar, write spinner + message, pad with spaces to clear old text
        buf.Append($"{Esc}{StatusBarRow};1H");
        var content = $" {ch} {msg}";
        buf.Append($"{BlackBg}{DimWhite}{content}{new string(' ', Math.Max(0, maxLeft - content.Length))}{Reset}");
        AppendCursorRestore(buf);
        Flush(buf);
    }

    /// <summary>Redraw the command bar.</summary>
    public void RedrawCommandBar(string text, int cursorPos)
    {
        _lastCommandText = text;
        _lastCursorPos = cursorPos;
        var newHeight = CommandBar.HeightForText(text, _termWidth);
        var maxHeight = Math.Max(1, (_termHeight - 6) / 2);
        newHeight = Math.Min(newHeight, maxHeight);

        if (newHeight != _commandBarHeight)
        {
            _commandBarHeight = newHeight;
            SetScrollRegion();
            UpdateStatusBar();
        }

        var lines = CommandBar.Render(text, _termWidth);
        var orangeBorder = new string('\u2500', _termWidth);
        var buf = new StringBuilder();

        buf.Append(HideCursor);

        // Orange top border
        buf.Append($"{Esc}{OrangeTopRow};1H{Esc}2K");
        buf.Append($"{Orange}{orangeBorder}{Reset}");

        // Command bar text lines
        for (var i = 0; i < _commandBarHeight; i++)
        {
            var row = CommandBarFirstRow + i;
            buf.Append($"{Esc}{row};1H{Esc}2K");
            if (i < lines.Length)
                buf.Append(lines[i]);
        }

        // Orange bottom border with persona + role labels on the right
        buf.Append($"{Esc}{OrangeBottomRow};1H{Esc}2K");
        var combinedLabel = _personaLabel + _roleLabel;
        if (combinedLabel.Length > 0 && combinedLabel.Length < _termWidth - 4)
        {
            var borderLen = _termWidth - combinedLabel.Length;
            buf.Append($"{Orange}{new string('\u2500', borderLen)}{Reset}");
            if (_personaLabel.Length > 0)
                buf.Append($"{OrangeBg}{Black}{_personaLabel}{Reset}");
            if (_roleLabel.Length > 0)
                buf.Append($"{OrangeBg}{Black}{_roleLabel}{Reset}");
        }
        else
        {
            buf.Append($"{Orange}{orangeBorder}{Reset}");
        }

        // Update cursor position and show it
        var (cursorRow, cursorCol) = CommandBar.CursorPosition(cursorPos, _termWidth);
        _lastCursorRow = CommandBarFirstRow + cursorRow;
        _lastCursorCol = cursorCol + 1;
        buf.Append($"{Esc}{_lastCursorRow};{_lastCursorCol}H");
        buf.Append(ShowCursor);
        Flush(buf);
    }

    /// <summary>Resize the command bar.</summary>
    public void ResizeCommandBar(int newHeight)
    {
        if (newHeight == _commandBarHeight) return;
        _commandBarHeight = Math.Max(1, newHeight);
        SetScrollRegion();
        UpdateStatusBar();
    }

    /// <summary>Clear the scroll region content.</summary>
    public void ClearContent()
    {
        var buf = new StringBuilder();
        buf.Append(HideCursor);
        for (var r = 1; r <= ScrollRegionEnd; r++)
            buf.Append($"{Esc}{r};1H{Esc}2K");
        buf.Append($"{Esc}{ScrollRegionEnd};1H");
        AppendCursorRestore(buf);
        Flush(buf);
    }

    /// <summary>Reset terminal to normal state.</summary>
    public void Dispose()
    {
        _resizeTimer?.Dispose();
        StatusBar.StopSpinner();

        Console.Write($"{Esc}r{Esc}{_termHeight};1H{ShowCursor}\n");
        Console.Out.Flush();
    }

    private void AppendCursorRestore(StringBuilder buf)
    {
        if (_lastCursorRow > 0)
            buf.Append($"{Esc}{_lastCursorRow};{_lastCursorCol}H");
        buf.Append(ShowCursor);
    }

    private static void Flush(StringBuilder buf)
    {
        Console.Write(buf.ToString());
        Console.Out.Flush();
    }

    private void SetScrollRegion()
    {
        Console.Write($"{Esc}1;{ScrollRegionEnd}r");
    }

    private void CheckResize()
    {
        var w = Console.WindowWidth;
        var h = Console.WindowHeight;
        if (w == _termWidth && h == _termHeight) return;

        _termWidth = w;
        _termHeight = h;

        // Full screen clear + layout rebuild
        var buf = new StringBuilder();
        buf.Append(HideCursor);

        // Remove scroll region temporarily so we can clear everything
        buf.Append($"{Esc}r");

        // Clear entire screen
        buf.Append($"{Esc}2J");

        // Move to top
        buf.Append($"{Esc}1;1H");

        Flush(buf);

        // Re-establish scroll region and redraw all fixed UI
        SetScrollRegion();
        UpdateStatusBar();
        RedrawCommandBar(_lastCommandText ?? "", _lastCursorPos);
    }

    // Track last command bar state for resize redraws
    private string? _lastCommandText;
    private int _lastCursorPos;
}
