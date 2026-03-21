using Daisi.Minion.Tui.Layout;

namespace Daisi.Minion.Tui;

/// <summary>
/// Handles line editing with history, word-wrapping via CommandBar,
/// and routes all display through LayoutManager.
/// </summary>
public sealed class InputHandler
{
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private LayoutManager? _layout;
    private ConsoleOutput? _output;
    private Action? _onCycleRole;
    private Action? _onCyclePersona;

    /// <summary>Attach the layout manager and output for word-wrapping command bar display.</summary>
    public void SetLayout(LayoutManager layout, ConsoleOutput output)
    {
        _layout = layout;
        _output = output;
    }

    /// <summary>Set the callback for Shift+Tab to cycle roles.</summary>
    public void OnCycleRole(Action callback) => _onCycleRole = callback;

    /// <summary>Set the callback for Ctrl+~ to cycle personas.</summary>
    public void OnCyclePersona(Action callback) => _onCyclePersona = callback;

    /// <summary>
    /// Read a line of input with history navigation and word-wrapping command bar.
    /// Returns null if Ctrl+C or Ctrl+D is pressed.
    /// </summary>
    public string? ReadLine()
    {
        var buffer = new List<char>();
        int cursor = 0;
        _historyIndex = _history.Count;

        // Draw initial empty command bar
        RedrawCommandBar(buffer, cursor);

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    var line = new string(buffer.ToArray());
                    if (!string.IsNullOrWhiteSpace(line))
                        _history.Add(line);
                    // Reset command bar to 1 line
                    buffer.Clear();
                    cursor = 0;
                    RedrawCommandBar(buffer, cursor);
                    return line;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.RemoveAt(cursor - 1);
                        cursor--;
                        RedrawCommandBar(buffer, cursor);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Count)
                    {
                        buffer.RemoveAt(cursor);
                        RedrawCommandBar(buffer, cursor);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursor > 0)
                    {
                        cursor--;
                        RedrawCommandBar(buffer, cursor);
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Count)
                    {
                        cursor++;
                        RedrawCommandBar(buffer, cursor);
                    }
                    break;

                case ConsoleKey.Home:
                    cursor = 0;
                    RedrawCommandBar(buffer, cursor);
                    break;

                case ConsoleKey.End:
                    cursor = buffer.Count;
                    RedrawCommandBar(buffer, cursor);
                    break;

                case ConsoleKey.UpArrow:
                    if (_historyIndex > 0)
                    {
                        _historyIndex--;
                        SetBuffer(buffer, _history[_historyIndex], ref cursor);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (_historyIndex < _history.Count - 1)
                    {
                        _historyIndex++;
                        SetBuffer(buffer, _history[_historyIndex], ref cursor);
                    }
                    else if (_historyIndex == _history.Count - 1)
                    {
                        _historyIndex = _history.Count;
                        SetBuffer(buffer, "", ref cursor);
                    }
                    break;

                case ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                    _onCycleRole?.Invoke();
                    break;

                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursor = 0;
                    RedrawCommandBar(buffer, cursor);
                    break;

                default:
                    if (key.KeyChar == '\x03' || key.KeyChar == '\x04') // Ctrl+C or Ctrl+D
                        return null;

                    // Ctrl+` / Ctrl+~ (Oem3 key with Ctrl)
                    if (key.Key == ConsoleKey.Oem3 && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                    {
                        _onCyclePersona?.Invoke();
                        break;
                    }

                    if (key.KeyChar >= 32) // Printable
                    {
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                        RedrawCommandBar(buffer, cursor);
                    }
                    break;
            }
        }
    }

    private void SetBuffer(List<char> buffer, string text, ref int cursor)
    {
        buffer.Clear();
        buffer.AddRange(text);
        cursor = buffer.Count;
        RedrawCommandBar(buffer, cursor);
    }

    private void RedrawCommandBar(List<char> buffer, int cursor)
    {
        var text = new string(buffer.ToArray());
        if (_output != null)
        {
            _output.RedrawCommandBar(text, cursor);
        }
        else
        {
            // Fallback: simple inline redraw (no layout manager)
            Console.Write("\r\x1b[2K");
            Console.Write($"> {text}");
            if (cursor < buffer.Count)
                Console.Write($"\x1b[{buffer.Count - cursor}D");
        }
    }
}
