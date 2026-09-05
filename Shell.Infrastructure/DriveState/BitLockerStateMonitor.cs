using System.Management;

namespace Shell.Infrastructure.DriveState;

public sealed class BitLockerStateMonitor : IDisposable
{
    private const int BitLockerDebounceMilliseconds = 250;
    private const int StartRetryMilliseconds = 1000;
    private const int MaxStartAttempts = 3;
    private const string BitLockerNamespace = @"\\.\Root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string BitLockerVolumeChangeQuery =
        "SELECT * FROM __InstanceModificationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_EncryptableVolume'";

    private readonly SynchronizationContext _uiContext;
    private readonly object _sync = new();
    private readonly HashSet<string> _pendingDriveRoots = new(StringComparer.OrdinalIgnoreCase);

    private ManagementEventWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private bool _pendingRefreshAll;
    private bool _startQueued;
    private int _startAttemptCount;
    private bool _disposed;

    public event EventHandler<string?>? BitLockerStateChanged;

    public BitLockerStateMonitor(SynchronizationContext uiContext)
    {
        _uiContext = uiContext;
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _startQueued || _watcher is not null)
                return;

            _startQueued = true;
            _startAttemptCount++;
        }

        _ = Task.Run(StartCore);
    }

    private void StartCore()
    {
        ManagementEventWatcher? watcher = null;

        try
        {
            ConnectionOptions options = new()
            {
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy
            };

            ManagementScope scope = new(BitLockerNamespace, options);
            WqlEventQuery query = new(BitLockerVolumeChangeQuery);

            watcher = new ManagementEventWatcher(scope, query);
            watcher.EventArrived += Watcher_EventArrived;
            watcher.Start();

            lock (_sync)
            {
                if (_disposed || _watcher is not null)
                {
                    _startQueued = false;
                    StopAndDisposeWatcher(watcher);
                    return;
                }

                _watcher = watcher;
                _startQueued = false;
                _startAttemptCount = 0;
                watcher = null;
            }
        }
        catch
        {
            if (watcher is not null)
                StopAndDisposeWatcher(watcher);

            bool retry;
            lock (_sync)
            {
                _startQueued = false;
                retry = !_disposed && _watcher is null && _startAttemptCount < MaxStartAttempts;
            }

            if (retry)
                _ = RetryStartAsync();
        }
    }

    private async Task RetryStartAsync()
    {
        try
        {
            await Task.Delay(StartRetryMilliseconds).ConfigureAwait(false);
            Start();
        }
        catch
        {
            // Best-effort monitor startup; callers retain manual refresh paths.
        }
    }

    private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        QueueBitLockerStateChanged(TryGetDriveRootFromEvent(e.NewEvent));
    }

    private void QueueBitLockerStateChanged(string? driveRoot)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                _pendingRefreshAll = true;
                _pendingDriveRoots.Clear();
            }
            else if (!_pendingRefreshAll)
            {
                _pendingDriveRoots.Add(DriveStateManager.NormalizeDriveRoot(driveRoot));
            }

            if (_debounceTimer is null)
            {
                _debounceTimer = new System.Threading.Timer(
                    _ => FlushBitLockerStateChanged(),
                    null,
                    BitLockerDebounceMilliseconds,
                    Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(BitLockerDebounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void FlushBitLockerStateChanged()
    {
        string? driveRootToRefresh;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_pendingRefreshAll || _pendingDriveRoots.Count != 1)
            {
                driveRootToRefresh = null;
            }
            else
            {
                driveRootToRefresh = _pendingDriveRoots.First();
            }

            _pendingRefreshAll = false;
            _pendingDriveRoots.Clear();
        }

        _uiContext.Post(_ =>
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
            }

            BitLockerStateChanged?.Invoke(this, driveRootToRefresh);
        }, null);
    }

    private static string? TryGetDriveRootFromEvent(ManagementBaseObject? eventObject)
    {
        if (eventObject is null)
            return null;

        return TryGetDriveRootFromInstance(eventObject["TargetInstance"] as ManagementBaseObject) ??
               TryGetDriveRootFromInstance(eventObject["PreviousInstance"] as ManagementBaseObject);
    }

    private static string? TryGetDriveRootFromInstance(ManagementBaseObject? instance)
    {
        try
        {
            string? driveLetter = instance?["DriveLetter"] as string;
            if (string.IsNullOrWhiteSpace(driveLetter))
                return null;

            driveLetter = driveLetter.Trim();

            if (driveLetter.Length == 2 && driveLetter[1] == ':')
                return driveLetter + "\\";

            return DriveStateManager.NormalizeDriveRoot(driveLetter);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        ManagementEventWatcher? watcher;
        System.Threading.Timer? debounceTimer;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            watcher = _watcher;
            debounceTimer = _debounceTimer;

            _watcher = null;
            _debounceTimer = null;
            _pendingRefreshAll = false;
            _pendingDriveRoots.Clear();
        }

        debounceTimer?.Dispose();

        if (watcher is not null)
            StopAndDisposeWatcher(watcher);
    }

    private static void StopAndDisposeWatcher(ManagementEventWatcher watcher)
    {
        try
        {
            watcher.Stop();
        }
        catch
        {
        }

        try
        {
            watcher.Dispose();
        }
        catch
        {
        }
    }
}
