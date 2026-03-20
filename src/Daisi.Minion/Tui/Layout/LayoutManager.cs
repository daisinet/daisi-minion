using System.Runtime.InteropServices;

namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Central layout coordinator using ANSI scroll regions (DECSTBM).
/// Manages three zones:
///   1. Content area (scroll region) — rows 1 to (H - reserved)
///   2. Status bar (black bg, floating) — with padding above and below
///   3. Command bar (1+ lines) — with orange top/bottom borders
///
/// Layout (bottom-up):
///   [content scroll region]
///   (blank padding)
///   status bar (black bg)
///   (blank padding)
///   ─── orange top border ───
///   command bar line(s)
///   ─── orange bottom border ───
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
    private Timer? _resizeTimer;
    private string _personaLabel = "";

    public StatusBar StatusBar { get; } = new();

    /// <summary>Set the persona label shown on the bottom border of the command bar.</summary>
    public void SetPersonaLabel(string name)
    {
        _personaLabel = string.IsNullOrEmpty(name) ? "" : $" {name} ";
    }

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

    /// <summary>
    /// Initialize the layout: enable VT100, set up scroll region, draw initial bars.
    /// </summary>
    public void Initialize()
    {
        EnableWindowsVt100();
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        _termWidth = Console.WindowWidth;
        _termHeight = Console.WindowHeight;

        SetScrollRegion();
        ClearContent();
        UpdateStatusBar();
        RedrawCommandBar("", 0);

        // Move cursor to the content area
        Console.Write($"{Esc}1;1H");
        Console.Out.Flush();

        _resizeTimer = new Timer(_ => CheckResize(), null, 250, 250);
    }

    /// <summary>Write text into the content scroll region.</summary>
    public void WriteToContent(string text)
    {
        Console.Write($"{HideCursor}");
        Console.Write($"{Esc}s");
        Console.Write($"{Esc}{ScrollRegionEnd};1H");
        Console.Write(text);
        Console.Write($"{Esc}u");
        Console.Write($"{ShowCursor}");
        Console.Out.Flush();
    }

    /// <summary>Redraw the status bar at its fixed row.</summary>
    public void UpdateStatusBar()
    {
        var content = StatusBar.Render(_termWidth);

        Console.Write($"{HideCursor}");
        Console.Write($"{Esc}s");

        // Blank padding above
        Console.Write($"{Esc}{PadAboveRow};1H{Esc}2K");

        // Status bar
        Console.Write($"{Esc}{StatusBarRow};1H{Esc}2K");
        Console.Write($"{BlackBg}{DimWhite}");
        if (content.Length >= _termWidth)
            Console.Write(content[.._termWidth]);
        else
            Console.Write(content + new string(' ', _termWidth - content.Length));
        Console.Write(Reset);

        // Blank padding below
        Console.Write($"{Esc}{PadBelowRow};1H{Esc}2K");

        Console.Write($"{Esc}u");
        RestoreCursorVisible();
        Console.Out.Flush();
    }

    /// <summary>Update only the spinner character. Minimal work, no flicker.</summary>
    public void UpdateSpinnerChar()
    {
        var ch = StatusBar.CurrentSpinnerChar;
        if (ch == null) return;

        Console.Write($"{HideCursor}");
        Console.Write($"{Esc}s");
        Console.Write($"{Esc}{StatusBarRow};{StatusBar.SpinnerCol}H");
        Console.Write($"{BlackBg}{DimWhite}{ch}{Reset}");
        Console.Write($"{Esc}u");
        RestoreCursorVisible();
        Console.Out.Flush();
    }

    /// <summary>Redraw the command bar.</summary>
    public void RedrawCommandBar(string text, int cursorPos)
    {
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

        Console.Write($"{HideCursor}");

        // Orange top border
        Console.Write($"{Esc}{OrangeTopRow};1H{Esc}2K");
        Console.Write($"{Orange}{orangeBorder}{Reset}");

        // Command bar text lines
        for (var i = 0; i < _commandBarHeight; i++)
        {
            var row = CommandBarFirstRow + i;
            Console.Write($"{Esc}{row};1H{Esc}2K");
            if (i < lines.Length)
                Console.Write(lines[i]);
        }

        // Orange bottom border with persona label on the right
        Console.Write($"{Esc}{OrangeBottomRow};1H{Esc}2K");
        if (_personaLabel.Length > 0 && _personaLabel.Length < _termWidth - 4)
        {
            var borderLen = _termWidth - _personaLabel.Length;
            Console.Write($"{Orange}{new string('\u2500', borderLen)}{Reset}");
            Console.Write($"{OrangeBg}{Black}{_personaLabel}{Reset}");
        }
        else
        {
            Console.Write($"{Orange}{orangeBorder}{Reset}");
        }

        // Position cursor in the command bar and show it
        var (cursorRow, cursorCol) = CommandBar.CursorPosition(cursorPos, _termWidth);
        _lastCursorRow = CommandBarFirstRow + cursorRow;
        _lastCursorCol = cursorCol + 1;
        Console.Write($"{Esc}{_lastCursorRow};{_lastCursorCol}H");
        Console.Write($"{ShowCursor}");
        Console.Out.Flush();
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
        Console.Write($"{HideCursor}");
        Console.Write($"{Esc}1;1H");
        for (var r = 1; r <= ScrollRegionEnd; r++)
        {
            Console.Write($"{Esc}{r};1H");
            Console.Write($"{Esc}2K");
        }
        Console.Write($"{Esc}{ScrollRegionEnd};1H");
        RestoreCursorVisible();
        Console.Out.Flush();
    }

    /// <summary>Reset terminal to normal state.</summary>
    public void Dispose()
    {
        _resizeTimer?.Dispose();
        StatusBar.StopSpinner();

        Console.Write($"{Esc}r");
        Console.Write($"{Esc}{_termHeight};1H");
        Console.Write($"{ShowCursor}");
        Console.WriteLine();
        Console.Out.Flush();
    }

    private void RestoreCursorVisible()
    {
        // Move cursor back to its last known command bar position
        if (_lastCursorRow > 0)
            Console.Write($"{Esc}{_lastCursorRow};{_lastCursorCol}H");
        Console.Write($"{ShowCursor}");
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

        SetScrollRegion();
        UpdateStatusBar();
    }
}
