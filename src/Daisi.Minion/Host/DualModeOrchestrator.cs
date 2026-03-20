using Daisi.Minion.Tui;
using Daisi.Llama.Chat;

namespace Daisi.Minion.Host;

/// <summary>
/// Orchestrates the dual-mode behavior of daisi-minion:
/// - Active (coding): Model + KV cache owned by the coding session.
/// - Idle (hosting): Model available to ORC consumers for inference.
/// Transitions are triggered by the ActivityMonitor.
/// </summary>
public sealed class DualModeOrchestrator : IAsyncDisposable
{
    private readonly ActivityMonitor _activityMonitor;
    private readonly HostModeService _hostService;
    private readonly AnsiRenderer _renderer;
    private DaisiLlamaModelHandle? _modelHandle;
    private Timer? _idleCheckTimer;

    public bool IsHostMode => _hostService.IsActive;

    public DualModeOrchestrator(
        ActivityMonitor activityMonitor,
        HostModeService hostService,
        AnsiRenderer renderer)
    {
        _activityMonitor = activityMonitor;
        _hostService = hostService;
        _renderer = renderer;

        _activityMonitor.OnIdleTimeout += OnIdleTimeout;
        _activityMonitor.OnActivityResumed += OnActivityResumed;
    }

    /// <summary>
    /// Start monitoring for idle/active transitions.
    /// </summary>
    public void Start(DaisiLlamaModelHandle modelHandle)
    {
        _modelHandle = modelHandle;

        // Check idle state every 30 seconds
        _idleCheckTimer = new Timer(_ => _activityMonitor.CheckIdle(),
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Record user activity (resets idle timer, exits host mode if needed).
    /// </summary>
    public void RecordActivity()
    {
        _activityMonitor.RecordActivity();
    }

    private async void OnIdleTimeout()
    {
        if (_modelHandle == null || _hostService.IsActive) return;

        try
        {
            await _hostService.EnterHostModeAsync(_modelHandle, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to enter host mode: {ex.Message}");
        }
    }

    private async void OnActivityResumed()
    {
        if (!_hostService.IsActive) return;

        try
        {
            await _hostService.LeaveHostModeAsync();
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"Failed to leave host mode: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_idleCheckTimer != null)
        {
            await _idleCheckTimer.DisposeAsync();
            _idleCheckTimer = null;
        }

        await _hostService.DisposeAsync();
    }
}
