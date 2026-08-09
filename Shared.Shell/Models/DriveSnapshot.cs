namespace Shared.Shell.Models;

public sealed record DriveSnapshot
{
    public required string DriveRoot { get; init; }
    public required string DisplayName { get; init; }
    public string? VolumeLabel { get; init; }

    public DriveType DriveType { get; init; }
    public bool IsReady { get; init; }
    public bool IsPresent { get; init; } = true;
    public bool IsSystemDrive { get; init; }

    public bool IsBitLockerProtected { get; init; }
    public bool IsBitLockerLocked { get; init; }

    public DriveIssueKind IssueKind { get; init; }
    public int? IssueHResult { get; init; }
    public string? IssueMessage { get; init; }

    public bool IsEffectivelyBitLockerLocked => IsBitLockerLocked || IssueKind == DriveIssueKind.BitLockerLocked;

    public long? TotalSizeBytes { get; init; }
    public long? FreeSpaceBytes { get; init; }

    public DriveVisualKind VisualKind { get; init; }
}