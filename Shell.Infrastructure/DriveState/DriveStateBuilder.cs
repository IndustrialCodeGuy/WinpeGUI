using Shell.Core.Models;

namespace Shell.Infrastructure.DriveState;

public sealed class DriveStateBuilder
{
    private readonly DriveStateManager _driveStateManager;

    public DriveStateBuilder(DriveStateManager driveStateManager)
    {
        _driveStateManager = driveStateManager;
    }

    public DriveSetSnapshot RefreshAndBuildSnapshot()
    {
        _driveStateManager.RefreshAll();
        return BuildSnapshotFromCurrentState();
    }

    public DriveSetSnapshot BuildSnapshotFromCurrentState()
    {
        return new DriveSetSnapshot
        {
            Drives = _driveStateManager.GetVisibleDrives(),
            RefreshedUtc = DateTime.UtcNow
        };
    }

    public DriveSetSnapshot RefreshDriveAndBuildSnapshot(string driveRoot)
    {
        _driveStateManager.RefreshDrive(driveRoot);
        return BuildSnapshotFromCurrentState();
    }

    public void RequestBitLockerStateRefresh(string driveRoot)
    {
        _driveStateManager.RequestBitLockerStateRefresh(driveRoot);
    }

    public void RequestBitLockerStatesRefresh()
    {
        _driveStateManager.RequestBitLockerStatesRefresh();
    }
}
