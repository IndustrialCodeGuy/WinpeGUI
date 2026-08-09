using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;
using Shell.Core.Interfaces;
using Shell.Core.Models;

namespace Shell.Infrastructure.DriveState;

public sealed class DriveStateStore : IDriveStateStore
{
    private readonly DriveStateBuilder _builder;
    private readonly object _gate = new();

    private DriveSetSnapshot _current = new()
    {
        Drives = Array.Empty<SharedDriveSnapshot>(),
        RefreshedUtc = DateTime.MinValue
    };

    public DriveStateStore(DriveStateBuilder builder)
    {
        _builder = builder;
    }

    public DriveSetSnapshot GetCurrentSnapshot()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public DriveSetSnapshot RefreshAll()
    {
        DriveSetSnapshot snapshot = _builder.RefreshAndBuildSnapshot();

        lock (_gate)
        {
            _current = snapshot;
            return _current;
        }
    }

    public DriveSetSnapshot RefreshDrive(string driveRoot)
    {
        DriveSetSnapshot snapshot = _builder.RefreshDriveAndBuildSnapshot(driveRoot);

        lock (_gate)
        {
            _current = snapshot;
            return _current;
        }
    }

    public DriveSetSnapshot RebuildFromCurrentSource()
    {
        DriveSetSnapshot snapshot = _builder.BuildSnapshotFromCurrentState();

        lock (_gate)
        {
            _current = snapshot;
            return _current;
        }
    }

    public void RequestBitLockerStateRefresh(string driveRoot)
    {
        _builder.RequestBitLockerStateRefresh(driveRoot);
    }

    public void RequestBitLockerStatesRefresh()
    {
        _builder.RequestBitLockerStatesRefresh();
    }

    public SharedDriveSnapshot? TryGetDrive(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            return null;

        lock (_gate)
        {
            return _current.Drives.FirstOrDefault(d =>
                string.Equals(d.DriveRoot, driveRoot, StringComparison.OrdinalIgnoreCase));
        }
    }
}