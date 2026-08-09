using Shared.Shell.Models;

namespace Explorer.UI.Shell;

internal enum ExplorerTreeNodeKind
{
    ThisPc,
    Drive,
    Folder
}

internal sealed class ExplorerTreeNodeTag
{
    public ExplorerTreeNodeKind Kind { get; init; }
    public string? Path { get; init; }

    // Folder nodes use the native TreeView cChildren flag instead of a real
    // placeholder child. Once a folder has been loaded and found empty, keep it
    // from being treated as lazy again.
    public bool TreeChildrenLoaded { get; set; }

    public DriveType? DriveType { get; init; }
    public bool? IsReady { get; init; }
    public bool? IsLocked { get; init; }
    public bool? IsBitLockerProtected { get; init; }
    public DriveIssueKind? IssueKind { get; init; }
    public int? IssueHResult { get; init; }
    public string? IssueMessage { get; init; }
}

internal enum ExplorerListRowKind
{
    Drive,
    Directory,
    File
}

internal sealed class ExplorerListRow
{
    public ExplorerListRowKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    public string TypeText { get; init; } = string.Empty;
    public string? Extension { get; init; }
    public bool IsVisibleHidden { get; init; }

    public DateTime? ModifiedLocalTime { get; init; }
    public long? SizeBytes { get; init; }

    public DriveType? DriveType { get; init; }
    public bool? IsReady { get; init; }
    public bool? IsLocked { get; init; }
    public bool? IsBitLockerProtected { get; init; }
    public DriveIssueKind? IssueKind { get; init; }
    public int? IssueHResult { get; init; }
    public string? IssueMessage { get; init; }
    public long? FreeSpaceBytes { get; init; }
    public long? TotalSizeBytes { get; init; }
    public DriveVisualKind? DriveVisualKind { get; init; }
}
