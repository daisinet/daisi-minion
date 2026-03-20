namespace Daisi.Minion.Host;

/// <summary>
/// Monitors user activity to determine when to switch between coding and host modes.
/// </summary>
public sealed class ActivityMonitor
{
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly TimeSpan _idleTimeout;

    public event Action? OnIdleTimeout;
    public event Action? OnActivityResumed;

    public bool IsIdle => (DateTime.UtcNow - _lastActivity) > _idleTimeout;
    public TimeSpan IdleDuration => DateTime.UtcNow - _lastActivity;

    public ActivityMonitor(TimeSpan idleTimeout)
    {
        _idleTimeout = idleTimeout;
    }

    /// <summary>
    /// Record user activity (keystroke, command, etc).
    /// </summary>
    public void RecordActivity()
    {
        var wasIdle = IsIdle;
        _lastActivity = DateTime.UtcNow;

        if (wasIdle)
            OnActivityResumed?.Invoke();
    }

    /// <summary>
    /// Check if the idle timeout has been reached.
    /// Call this periodically (e.g. every 30 seconds).
    /// </summary>
    public void CheckIdle()
    {
        if (IsIdle)
            OnIdleTimeout?.Invoke();
    }
}
