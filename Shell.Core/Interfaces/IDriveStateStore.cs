using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;
using Shell.Core.Models;

namespace Shell.Core.Interfaces;

public interface IDriveStateStore
{
    DriveSetSnapshot GetCurrentSnapshot();

    DriveSetSnapshot RefreshAll();
    DriveSetSnapshot RefreshDrive(string driveRoot);
    DriveSetSnapshot RebuildFromCurrentSource();

    void RequestBitLockerStateRefresh(string driveRoot);
    void RequestBitLockerStatesRefresh();

    SharedDriveSnapshot? TryGetDrive(string driveRoot);
}
