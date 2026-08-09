namespace Shell.Core.Models;
using Shared.Shell.Models;

public enum ExplorerCommandTargetKind
{
    None,
    BackgroundFolder,
    BackgroundThisPc,
    File,
    Folder,
    Drive,
    ThisPc
}

public sealed class ExplorerCommandContext
{
    public string WindowId { get; init; } = string.Empty;

    public ExplorerCommandTargetKind TargetKind { get; init; }
    public string? TargetPath { get; init; }
    public IReadOnlyList<string> SelectionPaths { get; init; } = Array.Empty<string>();

    public string? CurrentLocation { get; init; }
    public bool IsThisPcView { get; init; }
    public bool IsTreeTarget { get; init; }
    public bool IsBackground { get; init; }

    public DriveType? DriveType { get; init; }
    public bool? IsReady { get; init; }
    public bool? IsLocked { get; init; }
    public bool? IsBitLockerProtected { get; init; }
    public DriveIssueKind? IssueKind { get; init; }
    public int? IssueHResult { get; init; }
    public string? IssueMessage { get; init; }

    public bool CanUseExplorerBitLockerUi { get; init; }
    public bool CanEjectDriveDevice { get; init; }
    public bool CanPaste { get; init; }
    public bool CanCreateFolder { get; init; }
    public bool CanShowCurrentLocationProperties { get; init; }
    public bool IsNotepadAvailable { get; init; }

    public bool HasTargetPath => !string.IsNullOrWhiteSpace(TargetPath);
    public bool HasSelection => SelectionPaths.Count > 0;
}