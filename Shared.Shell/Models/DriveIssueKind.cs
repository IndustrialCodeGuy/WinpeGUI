namespace Shared.Shell.Models;

public enum DriveIssueKind
{
    None,

    OpticalNoMedia,
    RemovableNoMediaOrUnavailable,

    BitLockerLocked,
    BitLockerStatusUnavailableNotElevated,
    BitLockerStatusProviderUnavailable,
    BitLockerStatusCheckFailed,

    AccessDenied,
    UnrecognizedVolume,
    DeviceNotConnected,
    NotReady,
    IoError,
    Unknown
}
