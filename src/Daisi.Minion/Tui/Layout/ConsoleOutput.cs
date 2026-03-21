namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Thread-safe console write coordinator. All terminal output must go through this class
/// to prevent interleaving from async operations (streaming, spinner timer, etc.).
/// Routes content writes to the scroll region and status/command bar writes outside it.
/// </summary>
public sealed class ConsoleOutput
{
    private readonly object _writeLock = new();
    private readonly LayoutManager _layout;

    public ConsoleOutput(LayoutManager layout)
    {
        _layout = layout;
    }

    /// <summary>Write text into the content scroll region.</summary>
    public void WriteContent(string text)
    {
        lock (_writeLock)
        {
            _layout.WriteToContent(text);
        }
    }

    /// <summary>Write a line into the content scroll region.</summary>
    public void WriteContentLine(string text = "")
    {
        lock (_writeLock)
        {
            _layout.WriteToContent(text + "\n");
        }
    }

    /// <summary>Thread-safe status bar update — mutates status then redraws.</summary>
    public void UpdateStatus(Action<StatusBar> mutate)
    {
        lock (_writeLock)
        {
            mutate(_layout.StatusBar);
            _layout.UpdateStatusBar();
        }
    }

    /// <summary>Thread-safe spinner tick — redraws the full status bar to keep indicators current.</summary>
    public void TickSpinner()
    {
        lock (_writeLock)
        {
            _layout.UpdateStatusBar();
        }
    }

    /// <summary>Thread-safe redraw of the command bar.</summary>
    public void RedrawCommandBar(string text, int cursorPos)
    {
        lock (_writeLock)
        {
            _layout.RedrawCommandBar(text, cursorPos);
        }
    }

    /// <summary>Thread-safe arbitrary write under the lock (for direct ANSI sequences).</summary>
    public void WriteLocked(Action action)
    {
        lock (_writeLock)
        {
            action();
        }
    }
}
