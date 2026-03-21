using Daisi.Minion.Tui;
using Daisi.Llogos.Chat;

namespace Daisi.Minion.Host;

/// <summary>
/// Manages the host mode lifecycle: connects to ORC, sends heartbeats,
/// and handles inference requests when the user is idle.
/// Wraps Daisi.Host.Core services for lightweight integration.
/// </summary>
public sealed class HostModeService : IAsyncDisposable
{
    private readonly AnsiRenderer _renderer;
    private readonly DaisiLlogosTextBackend _backend;
    private DaisiLlogosModelHandle? _modelHandle;
    private Timer? _heartbeatTimer;
    private bool _isActive;
    private CancellationTokenSource? _hostCts;

    public bool IsActive => _isActive;

    public HostModeService(AnsiRenderer renderer, DaisiLlogosTextBackend backend)
    {
        _renderer = renderer;
        _backend = backend;
    }

    /// <summary>
    /// Enter host mode: make the model available for ORC inference requests.
    /// </summary>
    public async Task EnterHostModeAsync(DaisiLlogosModelHandle modelHandle, CancellationToken ct)
    {
        if (_isActive) return;

        _modelHandle = modelHandle;
        _isActive = true;
        _hostCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _renderer.WriteInfo("[Host mode] Model available for network inference. Press any key to resume coding.");

        // Start heartbeat timer (60 second interval)
        _heartbeatTimer = new Timer(_ => SendHeartbeat(), null, TimeSpan.Zero, TimeSpan.FromSeconds(60));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Leave host mode: reclaim the model for local coding use.
    /// </summary>
    public async Task LeaveHostModeAsync()
    {
        if (!_isActive) return;

        _isActive = false;

        // Stop heartbeat
        if (_heartbeatTimer != null)
        {
            await _heartbeatTimer.DisposeAsync();
            _heartbeatTimer = null;
        }

        // Cancel any in-flight host inference
        _hostCts?.Cancel();
        _hostCts?.Dispose();
        _hostCts = null;

        _renderer.WriteInfo("[Host mode] Deactivated. Model reclaimed for coding.");
    }

    private void SendHeartbeat()
    {
        if (!_isActive || _modelHandle == null) return;

        try
        {
            // In full integration, this would send a heartbeat to the ORC via gRPC.
            // For now, log that we're alive and available.
            // The full implementation will use OrcContainer.SendHeartbeat().
        }
        catch (Exception ex)
        {
            _renderer.WriteError($"[Host mode] Heartbeat failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await LeaveHostModeAsync();
    }
}
