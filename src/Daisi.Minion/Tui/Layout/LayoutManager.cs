using System.Runtime.InteropServices;

namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Central layout coordinator using ANSI scroll regions (DECSTBM).
/// Manages three zones:
///   1. Content area (scroll region) — rows 1 to (H - reserved)
///   2. Status bar (1 line, reverse video) — pinned below scroll region
///   3. Command bar (1+ lines) — pinned at bottom
///
/// Content written to the scroll region auto-scrolls without disturbing the bars.
/// Status/command bar updates: save cursor, jump outside region, draw, restore cursor.
/// </summary>
public sealed class LayoutManager : IDisposable
{
    private const string Esc = "\x1b[";

    private int _termWidth;
    private int _termHeight;
    private int _commandBarHeight = 1;
    private Timer? _resizeTimer;

    public StatusBar StatusBar { get; } = new();

    // Layout row calculations (1-based for ANSI)
    private int ReservedBottom => 1 + _commandBarHeight; // status bar + command bar lines
    private int ScrollRegionEnd => _termHeight - ReservedBottom;
    private int StatusBarRow => ScrollRegionEnd + 1;
    private int CommandBarFirstRow => StatusBarRow + 1;

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
        var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (GetConsoleMode(handle, out var mode))
            SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }

    /// <summary>
    /// Initialize the layout: enable VT100, set up scroll region, draw initial bars.
    /// Call once at startup.
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

        // Move cursor to the content area (top-left of scroll region)
        Console.Write($"{Esc}1;1H");
        Console.Out.Flush();

        // Poll for resize every 250ms
        _resizeTimer = new Timer(_ => CheckResize(), null, 250, 250);
    }

    /// <summary>Write text into the content scroll region. Caller must hold the write lock.</summary>
    public void WriteToContent(string text)
    {
        // Save cursor, move into scroll region, restore is not needed —
        // we always keep the cursor in the scroll region during content writes.
        // If we're currently positioned outside (e.g. after a bar update), move back.
        Console.Write($"{Esc}s"); // Save cursor
        Console.Write($"{Esc}{ScrollRegionEnd};1H"); // Move to bottom of scroll region
        Console.Write(text);
        Console.Out.Flush();
    }

    /// <summary>Redraw the status bar at its fixed row. Caller must hold the write lock.</summary>
    public void UpdateStatusBar()
    {
        var content = StatusBar.Render(_termWidth);
        Console.Write($"{Esc}s"); // Save cursor
        Console.Write($"{Esc}{StatusBarRow};1H"); // Move to status bar row
        Console.Write($"{Esc}7m"); // Reverse video
        // Ensure we fill exactly the terminal width
        if (content.Length >= _termWidth)
            Console.Write(content[.._termWidth]);
        else
            Console.Write(content + new string(' ', _termWidth - content.Length));
        Console.Write($"{Esc}0m"); // Reset style
        Console.Write($"{Esc}u"); // Restore cursor
        Console.Out.Flush();
    }

    /// <summary>Redraw the command bar. Caller must hold the write lock.</summary>
    public void RedrawCommandBar(string text, int cursorPos)
    {
        var newHeight = CommandBar.HeightForText(text, _termWidth);
        var maxHeight = Math.Max(1, (_termHeight - 2) / 2); // Cap at half terminal
        newHeight = Math.Min(newHeight, maxHeight);

        if (newHeight != _commandBarHeight)
        {
            _commandBarHeight = newHeight;
            SetScrollRegion();
            // Redraw status bar at its new position
            UpdateStatusBar();
        }

        var lines = CommandBar.Render(text, _termWidth);

        Console.Write($"{Esc}s"); // Save cursor
        for (var i = 0; i < _commandBarHeight; i++)
        {
            var row = CommandBarFirstRow + i;
            Console.Write($"{Esc}{row};1H"); // Move to row
            Console.Write($"{Esc}2K"); // Clear line
            if (i < lines.Length)
                Console.Write(lines[i]);
        }

        // Position cursor in the command bar
        var (cursorRow, cursorCol) = CommandBar.CursorPosition(cursorPos, _termWidth);
        var absRow = CommandBarFirstRow + cursorRow;
        var absCol = cursorCol + 1; // 1-based
        Console.Write($"{Esc}{absRow};{absCol}H");
        Console.Out.Flush();
    }

    /// <summary>Resize the command bar (e.g. when input wraps to more lines).</summary>
    public void ResizeCommandBar(int newHeight)
    {
        if (newHeight == _commandBarHeight) return;
        _commandBarHeight = Math.Max(1, newHeight);
        SetScrollRegion();
        UpdateStatusBar();
    }

    /// <summary>Clear the scroll region content (for /clear command).</summary>
    public void ClearContent()
    {
        Console.Write($"{Esc}s"); // Save cursor
        Console.Write($"{Esc}1;1H"); // Move to top of scroll region
        for (var r = 1; r <= ScrollRegionEnd; r++)
        {
            Console.Write($"{Esc}{r};1H");
            Console.Write($"{Esc}2K"); // Clear line
        }
        Console.Write($"{Esc}{ScrollRegionEnd};1H"); // Position at bottom of scroll region
        Console.Out.Flush();
    }

    /// <summary>Reset terminal to normal state. Call on shutdown.</summary>
    public void Dispose()
    {
        _resizeTimer?.Dispose();
        StatusBar.StopSpinner();

        // Reset scroll region to full terminal
        Console.Write($"{Esc}r");
        // Move to bottom
        Console.Write($"{Esc}{_termHeight};1H");
        Console.WriteLine();
        Console.Out.Flush();
    }

    private void SetScrollRegion()
    {
        // DECSTBM: set scroll region from row 1 to ScrollRegionEnd
        Console.Write($"{Esc}1;{ScrollRegionEnd}r");
    }

    private void CheckResize()
    {
        var w = Console.WindowWidth;
        var h = Console.WindowHeight;
        if (w == _termWidth && h == _termHeight) return;

        _termWidth = w;
        _termHeight = h;

        // Reapply scroll region and redraw fixed elements
        SetScrollRegion();
        UpdateStatusBar();
    }
}
