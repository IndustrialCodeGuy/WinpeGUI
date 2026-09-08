namespace Shared.Shell.Models;

public enum DriveIssueKind
{
    None,

    OpticalNoMedia,
    RemovableNoMediaOrUnavailable,

    BitLockerLocked,

    AccessDenied,
    UnrecognizedVolume,
    DeviceNotConnected,
    NotReady,
    IoError,
    Unknown
}
