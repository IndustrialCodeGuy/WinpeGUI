using SharedDriveSnapshot = Shared.Shell.Models.DriveSnapshot;

namespace Shell.Core.Models;

public sealed class DriveSetSnapshot
{
    public IReadOnlyList<SharedDriveSnapshot> Drives { get; init; } = Array.Empty<SharedDriveSnapshot>();
    public DateTime RefreshedUtc { get; init; }
}
