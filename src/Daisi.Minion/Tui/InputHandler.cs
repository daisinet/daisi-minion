namespace Daisi.Minion.Tui;

/// <summary>
/// Handles line editing with history and basic readline-style features.
/// </summary>
public sealed class InputHandler
{
    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    /// <summary>
    /// Read a line of input with history navigation (up/down arrows).
    /// Returns null if Ctrl+C or Ctrl+D is pressed.
    /// </summary>
    public string? ReadLine()
    {
        var buffer = new List<char>();
        int cursor = 0;
        _historyIndex = _history.Count;

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    var line = new string(buffer.ToArray());
                    if (!string.IsNullOrWhiteSpace(line))
                        _history.Add(line);
                    return line;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.RemoveAt(cursor - 1);
                        cursor--;
                        RedrawLine(buffer, cursor);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Count)
                    {
                        buffer.RemoveAt(cursor);
                        RedrawLine(buffer, cursor);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursor > 0) { cursor--; Console.Write("\x1b[D"); }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Count) { cursor++; Console.Write("\x1b[C"); }
                    break;

                case ConsoleKey.Home:
                    if (cursor > 0)
                    {
                        Console.Write($"\x1b[{cursor}D");
                        cursor = 0;
                    }
                    break;

                case ConsoleKey.End:
                    if (cursor < buffer.Count)
                    {
                        Console.Write($"\x1b[{buffer.Count - cursor}C");
                        cursor = buffer.Count;
                    }
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

                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursor = 0;
                    RedrawLine(buffer, cursor);
                    break;

                default:
                    if (key.KeyChar == '\x03' || key.KeyChar == '\x04') // Ctrl+C or Ctrl+D
                        return null;

                    if (key.KeyChar >= 32) // Printable
                    {
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                        if (cursor == buffer.Count)
                            Console.Write(key.KeyChar);
                        else
                            RedrawLine(buffer, cursor);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Read multi-line input. Empty line or Ctrl+D ends input.
    /// </summary>
    public string? ReadMultiLine()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = ReadLine();
            if (line == null) return null;
            if (line == "" && lines.Count > 0) break;
            lines.Add(line);
        }
        return string.Join('\n', lines);
    }

    private static void SetBuffer(List<char> buffer, string text, ref int cursor)
    {
        // Move cursor to start, clear, write new text
        if (cursor > 0)
            Console.Write($"\x1b[{cursor}D");
        Console.Write("\x1b[0K"); // Clear to end
        buffer.Clear();
        buffer.AddRange(text);
        Console.Write(text);
        cursor = buffer.Count;
    }

    private static void RedrawLine(List<char> buffer, int cursor)
    {
        Console.Write("\r\x1b[2K"); // Clear entire line
        // We don't know the prompt, so just write from beginning
        // The caller should re-render the prompt if needed
        var text = new string(buffer.ToArray());
        Console.Write(text);
        if (cursor < buffer.Count)
            Console.Write($"\x1b[{buffer.Count - cursor}D");
    }
}
