namespace Daisi.Minion.Tui.Layout;

/// <summary>
/// Status line state and rendering. Shows a spinner/status on the left
/// and key-value indicators (model, branch, token count) on the right.
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

    private static readonly string[] SpinnerFrames =
        ["\u28cb", "\u2819", "\u2839", "\u2838", "\u283c", "\u2834", "\u2826", "\u2827", "\u2807", "\u280f"];

    /// <summary>The spinner character column (1-based) within the rendered line.</summary>
    public const int SpinnerCol = 2; // " X ..." — spinner char is at column 2

    public bool IsSpinning => _spinnerActive;

    /// <summary>Current spinner frame character, or null if not spinning.</summary>
    public string? CurrentSpinnerChar => _spinnerActive
        ? SpinnerFrames[_spinnerFrame]
        : null;

    /// <summary>Set the left-side status text (shown when spinner is not active).</summary>
    public void SetStatus(string text) => _statusText = text;

    /// <summary>Start an animated spinner with a message. Calls onTick from timer thread on each frame advance.</summary>
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

    /// <summary>Stop the spinner and revert to static status text.</summary>
    public void StopSpinner()
    {
        _spinnerActive = false;
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    /// <summary>Set or update a right-side indicator.</summary>
    public void SetIndicator(string key, string value) => _indicators[key] = value;

    /// <summary>Remove a right-side indicator.</summary>
    public void RemoveIndicator(string key) => _indicators.Remove(key);

    /// <summary>
    /// Render the full status bar content for the given width.
    /// </summary>
    public string Render(int width)
    {
        if (width <= 0) return "";

        var left = _spinnerActive
            ? $" {SpinnerFrames[_spinnerFrame]} {_spinnerMessage}"
            : $" {_statusText}";

        var right = _indicators.Count > 0
            ? string.Join(" \u2502 ", _indicators.Values) + " "
            : "";

        var gap = width - left.Length - right.Length;
        if (gap < 0)
        {
            var maxLeft = width - right.Length - 1;
            if (maxLeft > 3)
                left = left[..maxLeft];
            else
                right = "";
            gap = width - left.Length - right.Length;
        }

        return left + new string(' ', Math.Max(0, gap)) + right;
    }
}
