using System.Runtime.InteropServices;
using System.Text;

namespace Daisi.Minion.Tui;

/// <summary>
/// Lightweight inline spinner for startup steps (before the TUI layout is initialized).
/// Shows a Braille spinner + message on the current console line, updating in place.
/// Call Update() to change the message, then Finish() to complete with a checkmark.
/// </summary>
public sealed class StartupSpinner : IDisposable
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private const string Dim = "\x1b[90m";
    private const string Green = "\x1b[32m";
    private const string Reset = "\x1b[0m";

    private static bool _consoleReady;
    private readonly Timer _timer;
    private int _frame;
    private string _message = "";
    private bool _disposed;

    public StartupSpinner(string message)
    {
        EnsureConsoleReady();
        _message = message;
        Render();
        _timer = new Timer(_ =>
        {
            if (_disposed) return;
            _frame = (_frame + 1) % Frames.Length;
            Render();
        }, null, 80, 80);
    }

    /// <summary>Change the spinner message.</summary>
    public void Update(string message)
    {
        _message = message;
        Render();
    }

    /// <summary>Stop the spinner and show a completed checkmark.</summary>
    public void Finish(string? message = null)
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        var msg = message ?? _message;
        Console.Write($"\r\x1b[2K  {Green}✓{Reset} {msg}\n");
    }

    /// <summary>Stop the spinner and show an error.</summary>
    public void Fail(string message)
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        Console.Write($"\r\x1b[2K  \x1b[31m✗{Reset} {message}\n");
    }

    private void Render()
    {
        Console.Write($"\r\x1b[2K  {Dim}{Frames[_frame]}{Reset} {_message}");
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    /// <summary>
    /// One-time setup: UTF-8 encoding + Windows VT100 mode.
    /// Must run before any Unicode or ANSI output.
    /// </summary>
    private static void EnsureConsoleReady()
    {
        if (_consoleReady) return;
        _consoleReady = true;

        Console.OutputEncoding = Encoding.UTF8;

        if (OperatingSystem.IsWindows())
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (GetConsoleMode(handle, out var mode))
                SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint handle, uint mode);
}
