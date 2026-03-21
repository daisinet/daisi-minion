namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Status line state and rendering. Shows a spinner/status on the left,
/// a color-coded context usage bar, and key-value indicators on the right.
/// </summary>
public sealed class StatusBar
{
    private readonly Dictionary<string, string> _indicators = new();
    private string _statusText = "";
    private bool _spinnerActive;
    private string _spinnerMessage = "";
    private int _spinnerFrame;
    private Timer? _spinnerTimer;
    private Action? _onSpinnerTick;

    // Context usage tracking
    private int _contextUsed;
    private int _contextMax;

    private static readonly string[] SpinnerFrames =
        ["\u280b", "\u2819", "\u2839", "\u2838", "\u283c", "\u2834", "\u2826", "\u2827", "\u2807", "\u280f"];

    // Color thresholds (ANSI 256-color)
    private const string Green = "\x1b[38;5;34m";
    private const string Yellow = "\x1b[38;5;226m";
    private const string Orange = "\x1b[38;5;208m";
    private const string Red = "\x1b[38;5;196m";
    private const string Reset = "\x1b[0m";

    public const int SpinnerCol = 2;
    public bool IsSpinning => _spinnerActive;

    public string? CurrentSpinnerChar => _spinnerActive
        ? SpinnerFrames[_spinnerFrame]
        : null;

    public void SetStatus(string text) => _statusText = text;

    public void StartSpinner(string message, Action onTick)
    {
        _spinnerMessage = message;
        _spinnerActive = true;
        _spinnerFrame = 0;
        _onSpinnerTick = onTick;
        _spinnerTimer?.Dispose();
        _spinnerTimer = new Timer(_ =>
        {
            _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
            _onSpinnerTick?.Invoke();
        }, null, 120, 120);
    }

    public void StopSpinner()
    {
        _spinnerActive = false;
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    /// <summary>Update the context usage for the progress bar.</summary>
    public void SetContextUsage(int used, int max)
    {
        _contextUsed = used;
        _contextMax = max;
    }

    public void SetIndicator(string key, string value) => _indicators[key] = value;
    public void RemoveIndicator(string key) => _indicators.Remove(key);

    /// <summary>
    /// Render the full status bar content for the given width.
    /// The context bar is rendered between the left side and right indicators.
    /// ANSI color codes are included inline for the context bar.
    /// </summary>
    public string Render(int width)
    {
        if (width <= 0) return "";

        var left = _spinnerActive
            ? $" {SpinnerFrames[_spinnerFrame]} {_spinnerMessage}"
            : $" {_statusText}";

        // Build right side: indicators
        var right = _indicators.Count > 0
            ? string.Join(" \u2502 ", _indicators.Values) + " "
            : "";

        // Build context bar: [████░░░░] 2.4k/8k
        var contextBar = RenderContextBar();

        // Layout: left ... contextBar │ right
        var middle = contextBar.Length > 0
            ? $" {contextBar} "
            : "";

        // Calculate visible widths (strip ANSI codes for width calculation)
        var middleVisible = StripAnsi(middle);
        var totalUsed = left.Length + middleVisible.Length + right.Length;
        var gap = width - totalUsed;

        if (gap < 0)
        {
            var maxLeft = width - middleVisible.Length - right.Length - 1;
            if (maxLeft > 3)
                left = left[..maxLeft];
            else
                middle = "";
            gap = width - left.Length - StripAnsi(middle).Length - right.Length;
        }

        return left + new string(' ', Math.Max(0, gap)) + middle + right;
    }

    private string RenderContextBar()
    {
        if (_contextMax <= 0) return "";

        const int barWidth = 8;
        var pct = Math.Clamp((double)_contextUsed / _contextMax, 0, 1);
        var filled = (int)(pct * barWidth);
        var empty = barWidth - filled;

        var color = pct switch
        {
            < 0.50 => Green,
            < 0.75 => Yellow,
            < 0.90 => Orange,
            _ => Red,
        };

        var usedStr = FormatTokenCount(_contextUsed);
        var maxStr = FormatTokenCount(_contextMax);

        return $"{color}[{new string('\u2588', filled)}{new string('\u2591', empty)}] {usedStr}/{maxStr}{Reset}";
    }

    private static string FormatTokenCount(int tokens)
    {
        if (tokens >= 1000)
            return $"{tokens / 1000.0:F1}k";
        return tokens.ToString();
    }

    private static string StripAnsi(string text)
    {
        // Remove ANSI escape sequences for width calculation
        var result = new System.Text.StringBuilder();
        var inEsc = false;
        foreach (var c in text)
        {
            if (c == '\x1b') { inEsc = true; continue; }
            if (inEsc) { if (char.IsLetter(c)) inEsc = false; continue; }
            result.Append(c);
        }
        return result.ToString();
    }
}
