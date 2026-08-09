using Shell.Core.Interfaces;
using Shell.Core.Models;

namespace Shell.Infrastructure.DriveState;

public sealed class DriveTopologyMonitor : IDisposable
{
    private const int TopologyDebounceMilliseconds = 150;

    private readonly SynchronizationContext _uiContext;
    private readonly object _sync = new();

    private DriveTopologyMessageWindow? _window;
    private System.Threading.Timer? _debounceTimer;
    private RefreshReason? _pendingReason;
    private bool _disposed;

    public event EventHandler<RefreshReason>? TopologyChanged;

    public DriveTopologyMonitor(SynchronizationContext uiContext)
    {
        _uiContext = uiContext;
    }

    public void Start()
    {
        _window ??= new DriveTopologyMessageWindow(code =>
        {
            RefreshReason reason = code switch
            {
                0x8000 => RefreshReason.DeviceArrival,
                0x8004 => RefreshReason.DeviceRemoval,
                0x0007 => RefreshReason.DeviceNodesChanged,
                _ => RefreshReason.Unknown
            };

            QueueTopologyChanged(reason);
        });
    }

    private void QueueTopologyChanged(RefreshReason reason)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _pendingReason = MergeReason(_pendingReason, reason);

            if (_debounceTimer is null)
            {
                _debounceTimer = new System.Threading.Timer(_ => FlushTopologyChanged(), null, TopologyDebounceMilliseconds, Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(TopologyDebounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void FlushTopologyChanged()
    {
        RefreshReason reason;

        lock (_sync)
        {
            if (_disposed)
                return;

            reason = _pendingReason ?? RefreshReason.Unknown;
            _pendingReason = null;
        }

        _uiContext.Post(_ =>
        {
            if (_disposed)
                return;

            TopologyChanged?.Invoke(this, reason);
        }, null);
    }

    private static RefreshReason MergeReason(RefreshReason? current, RefreshReason next)
    {
        if (current is null || current == RefreshReason.Unknown)
            return next;

        if (next == RefreshReason.Unknown)
            return current.Value;

        if (current == next)
            return next;

        return RefreshReason.DeviceNodesChanged;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            _window?.Dispose();
            _window = null;

            _pendingReason = null;
        }
    }
}