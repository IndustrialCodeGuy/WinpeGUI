using Shell.Core.Models;
using Shell.Infrastructure.DriveState;

namespace Shell.Infrastructure.Coordination;

public enum StorageChangeKind
{
    Topology,
    BitLocker
}

public sealed class StorageChangeEventArgs : EventArgs
{
    public StorageChangeEventArgs(StorageChangeKind kind, RefreshReason reason, string? driveRoot)
    {
        Kind = kind;
        Reason = reason;
        DriveRoot = driveRoot;
    }

    public StorageChangeKind Kind { get; }
    public RefreshReason Reason { get; }
    public string? DriveRoot { get; }
}

/// <summary>
/// Shared shell-level storage notification source. The underlying monitors already
/// debounce their native event streams; this class gives consumers one lifecycle
/// and one event surface without centralizing their process-specific refresh work.
/// </summary>
public sealed class StorageChangeCoordinator : IDisposable
{
    private readonly DriveTopologyMonitor _driveTopologyMonitor;
    private readonly BitLockerStateMonitor? _bitLockerStateMonitor;
    private bool _started;
    private bool _disposed;

    public StorageChangeCoordinator(SynchronizationContext uiContext, bool monitorBitLocker)
    {
        ArgumentNullException.ThrowIfNull(uiContext);

        _driveTopologyMonitor = new DriveTopologyMonitor(uiContext);
        if (monitorBitLocker)
            _bitLockerStateMonitor = new BitLockerStateMonitor(uiContext);

        _driveTopologyMonitor.TopologyChanged += DriveTopologyMonitor_TopologyChanged;
        if (_bitLockerStateMonitor is not null)
            _bitLockerStateMonitor.BitLockerStateChanged += BitLockerStateMonitor_BitLockerStateChanged;
    }

    public event EventHandler<StorageChangeEventArgs>? StorageChanged;

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StorageChangeCoordinator));

        if (_started)
            return;

        _started = true;
        _driveTopologyMonitor.Start();
        _bitLockerStateMonitor?.Start();
    }

    private void DriveTopologyMonitor_TopologyChanged(object? sender, RefreshReason reason)
    {
        if (_disposed)
            return;

        StorageChanged?.Invoke(this, new StorageChangeEventArgs(StorageChangeKind.Topology, reason, null));
    }

    private void BitLockerStateMonitor_BitLockerStateChanged(object? sender, string? driveRoot)
    {
        if (_disposed)
            return;

        StorageChanged?.Invoke(this, new StorageChangeEventArgs(StorageChangeKind.BitLocker, RefreshReason.Unknown, driveRoot));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _driveTopologyMonitor.TopologyChanged -= DriveTopologyMonitor_TopologyChanged;
        if (_bitLockerStateMonitor is not null)
            _bitLockerStateMonitor.BitLockerStateChanged -= BitLockerStateMonitor_BitLockerStateChanged;

        _bitLockerStateMonitor?.Dispose();
        _driveTopologyMonitor.Dispose();
    }
}
