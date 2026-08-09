using Shared.Shell.Models;
using Shell.Core.FileTypes;
using Shell.Core.Interfaces;
using Shell.Core.Models;

namespace Shell.Infrastructure.Explorer;

public sealed class ExplorerCommandService : IExplorerCommandService
{
    private readonly IExplorerShellCommands _commands;
    private readonly IExplorerFileAssociationService _fileAssociationService;

    public ExplorerCommandService(IExplorerShellCommands commands, IExplorerFileAssociationService fileAssociationService)
    {
        _commands = commands;
        _fileAssociationService = fileAssociationService;
    }

    public IReadOnlyList<ExplorerMenuItemModel> BuildContextMenu(ExplorerCommandContext context)
    {
        return context.TargetKind switch
        {
            ExplorerCommandTargetKind.File => BuildFileMenu(context),
            ExplorerCommandTargetKind.Folder => BuildFolderMenu(context),
            ExplorerCommandTargetKind.Drive => BuildDriveMenu(context),
            ExplorerCommandTargetKind.BackgroundFolder => BuildBackgroundFolderMenu(context),
            ExplorerCommandTargetKind.BackgroundThisPc or ExplorerCommandTargetKind.ThisPc => BuildBackgroundThisPcMenu(),
            _ => Array.Empty<ExplorerMenuItemModel>()
        };
    }

    public bool TryExecute(ExplorerCommandId commandId, ExplorerCommandContext context, string? commandArgument = null)
    {
        switch (commandId)
        {
            case ExplorerCommandId.Open:
                if (!context.HasTargetPath || context.TargetKind != ExplorerCommandTargetKind.File)
                    return false;

                _commands.OpenFileSystemItem(context.TargetPath!);
                return true;

            case ExplorerCommandId.OpenWith:
                if (!context.HasTargetPath)
                    return false;

                _commands.OpenItemWith(context.TargetPath!);
                return true;

            case ExplorerCommandId.EditInNotepad:
                if (!context.HasTargetPath)
                    return false;

                _commands.EditFileInNotepad(context.TargetPath!);
                return true;

            case ExplorerCommandId.ExtraFileVerb:
                if (!context.HasTargetPath || string.IsNullOrWhiteSpace(commandArgument))
                    return false;

                return TryExecuteExtraFileVerb(context.TargetPath!, commandArgument);

            case ExplorerCommandId.OpenInNewWindow:
                if (string.IsNullOrWhiteSpace(context.TargetPath))
                    return true;

                if (context.TargetKind == ExplorerCommandTargetKind.Drive)
                {
                    if (context.DriveType == DriveType.CDRom &&
                        context.IssueKind == DriveIssueKind.OpticalNoMedia)
                    {
                        _commands.ShowOpticalDriveEmptyMessage(context.TargetPath);
                        return true;
                    }

                    if (context.IsLocked == true)
                    {
                        if (!_commands.CanUseExplorerBitLockerUi)
                        {
                            _commands.ShowDriveNotReadyMessage(
                            context.TargetPath,
                            context.IssueKind,
                            context.IssueMessage);
                            return true;
                        }

                        _commands.LaunchBitLockerHelper(
                        context.TargetPath,
                        ExplorerBitLockerAction.Unlock,
                        context.TargetPath,
                        context.WindowId,
                        openInNewWindowAfterUnlock: true);
                        return true;
                    }

                    if (HasDriveIssue(context.IssueKind) || context.IsReady == false)
                    {
                        _commands.ShowDriveNotReadyMessage(
                            context.TargetPath,
                            context.IssueKind,
                            context.IssueMessage);
                        return true;
                    }
                }

                _commands.OpenNewWindow(context.TargetPath);
                return true;

            case ExplorerCommandId.Cut:
                {
                    IReadOnlyList<string> paths = GetSubjectPaths(context);
                    if (paths.Count == 0)
                        return false;

                    _commands.SetClipboardFileTransfer(paths, move: true);
                    return true;
                }

            case ExplorerCommandId.Copy:
                {
                    IReadOnlyList<string> paths = GetSubjectPaths(context);
                    if (paths.Count == 0)
                        return false;

                    _commands.SetClipboardFileTransfer(paths, move: false);
                    return true;
                }

            case ExplorerCommandId.Paste:
                {
                    string? destination = GetPasteDestination(context);
                    if (string.IsNullOrWhiteSpace(destination))
                        return false;

                    _commands.PasteFileTransfer(destination);
                    return true;
                }

            case ExplorerCommandId.CopyAsPath:
                {
                    IReadOnlyList<string> paths = GetSubjectPaths(context);
                    if (paths.Count == 0)
                        return false;

                    _commands.CopyPathsToClipboard(paths);
                    return true;
                }

            case ExplorerCommandId.Delete:
                {
                    IReadOnlyList<string> paths = GetSubjectPaths(context);
                    if (paths.Count == 0)
                        return false;

                    _commands.DeletePaths(paths);
                    return true;
                }

            case ExplorerCommandId.Refresh:
                if (string.IsNullOrWhiteSpace(context.WindowId))
                    return false;

                _commands.RefreshWindow(context.WindowId);
                return true;

            case ExplorerCommandId.Properties:
                if (context.TargetKind == ExplorerCommandTargetKind.BackgroundFolder)
                {
                    if (string.IsNullOrWhiteSpace(context.CurrentLocation))
                        return false;

                    _commands.OpenItemProperties(context.CurrentLocation);
                    return true;
                }

                if (!context.HasTargetPath)
                    return false;

                _commands.OpenItemProperties(context.TargetPath!);
                return true;

            case ExplorerCommandId.BitLocker:
                if (!context.HasTargetPath)
                    return false;

                if (!context.CanUseExplorerBitLockerUi || !TryGetBitLockerContextAction(
                    context.IsReady,
                    context.IsLocked,
                    context.IsBitLockerProtected,
                    out _,
                    out ExplorerBitLockerAction action))
                {
                    return false;
                }

                _commands.LaunchBitLockerHelper(context.TargetPath!, action);
                return true;

            case ExplorerCommandId.FormatDrive:
                if (!context.HasTargetPath)
                    return false;

                _commands.FormatDrive(context.TargetPath!);
                return true;

            case ExplorerCommandId.EjectOrDisconnectDrive:
                if (!context.HasTargetPath)
                    return false;

                _commands.EjectOrDisconnectDrive(context.TargetPath!);
                return true;

            case ExplorerCommandId.NewFolder:
                if (string.IsNullOrWhiteSpace(context.CurrentLocation))
                    return false;

                return _commands.CreateNewFolder(context.CurrentLocation) is not null;

            case ExplorerCommandId.Rename:
                // Presenter/UI-owned command (inline rename).
                return false;

            default:
                return false;
        }
    }

    private IReadOnlyList<ExplorerMenuItemModel> BuildFileMenu(ExplorerCommandContext context)
    {
        bool hasSubject = HasSubjectPaths(context);

        List<ExplorerMenuItemModel> items =
        [
            ExplorerMenuItemModel.Command(ExplorerCommandId.Open, "Open", enabled: context.HasTargetPath),
            ExplorerMenuItemModel.Command(ExplorerCommandId.OpenWith, "Open with...", enabled: context.HasTargetPath)
        ];

        foreach (ExplorerMenuItemModel extraVerbItem in BuildExtraFileVerbMenuItems(context))
        {
            items.Add(extraVerbItem);
        }

        items.Add(ExplorerMenuItemModel.Command(
            ExplorerCommandId.EditInNotepad,
            "Edit in Notepad",
            enabled: context.HasTargetPath && context.IsNotepadAvailable));

        items.Add(ExplorerMenuItemModel.Separator());

        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Cut, "Cut", enabled: hasSubject));
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Copy, "Copy", enabled: hasSubject));
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.CopyAsPath, "Copy as path", enabled: hasSubject));

        items.Add(ExplorerMenuItemModel.Separator());

        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Rename, "Rename", enabled: context.HasTargetPath));
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Delete, "Delete", enabled: hasSubject));

        items.Add(ExplorerMenuItemModel.Separator());

        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Properties, "Properties", enabled: context.HasTargetPath));

        return items;
    }

    private static IReadOnlyList<ExplorerMenuItemModel> BuildFolderMenu(ExplorerCommandContext context)
    {
        bool hasSubject = HasSubjectPaths(context);
        bool canPaste = context.HasTargetPath && context.CanPaste;

        return
        [
            ExplorerMenuItemModel.Command(ExplorerCommandId.Open, "Open", enabled: context.HasTargetPath),
            ExplorerMenuItemModel.Command(ExplorerCommandId.OpenInNewWindow, "Open in New Window", enabled: context.HasTargetPath),

            ExplorerMenuItemModel.Separator(),

            ExplorerMenuItemModel.Command(ExplorerCommandId.Cut, "Cut", enabled: hasSubject),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Copy, "Copy", enabled: hasSubject),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Paste, "Paste", enabled: canPaste),
            ExplorerMenuItemModel.Command(ExplorerCommandId.CopyAsPath, "Copy as path", enabled: hasSubject),

            ExplorerMenuItemModel.Separator(),

            ExplorerMenuItemModel.Command(ExplorerCommandId.Rename, "Rename", enabled: context.HasTargetPath),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Delete, "Delete", enabled: hasSubject),

            ExplorerMenuItemModel.Separator(),

            ExplorerMenuItemModel.Command(ExplorerCommandId.Properties, "Properties", enabled: context.HasTargetPath)
        ];
    }

    private static List<ExplorerMenuItemModel> BuildDriveMenu(ExplorerCommandContext context)
    {
        List<ExplorerMenuItemModel> items =
        [
            ExplorerMenuItemModel.Command(ExplorerCommandId.Open, "Open", enabled: context.HasTargetPath),
            ExplorerMenuItemModel.Command(ExplorerCommandId.OpenInNewWindow, "Open in New Window", enabled: context.HasTargetPath),
            ExplorerMenuItemModel.Separator()
        ];

        if (context.CanUseExplorerBitLockerUi && TryGetBitLockerContextAction(
            context.IsReady,
            context.IsLocked,
            context.IsBitLockerProtected,
            out string bitLockerText,
            out _))
        {
            items.Add(ExplorerMenuItemModel.Command(
                ExplorerCommandId.BitLocker,
                bitLockerText,
                enabled: context.HasTargetPath));
        }

        bool isLockedDrive = context.IsLocked == true;

        if (context.DriveType is not DriveType.CDRom and not DriveType.Network)
        {
            items.Add(ExplorerMenuItemModel.Command(
                ExplorerCommandId.FormatDrive,
                "Format",
                enabled: context.HasTargetPath && !isLockedDrive));
        }

        bool canEjectOrDisconnect =
            context.DriveType == DriveType.Network ||
            context.DriveType == DriveType.CDRom ||
            context.CanEjectDriveDevice;

        if (canEjectOrDisconnect)
        {
            items.Add(ExplorerMenuItemModel.Command(
                ExplorerCommandId.EjectOrDisconnectDrive,
                context.DriveType == DriveType.Network ? "Disconnect" : "Eject",
                enabled: context.HasTargetPath));
        }

        bool hasSubject = HasSubjectPaths(context);
        bool canPaste = context.HasTargetPath && context.CanPaste && !isLockedDrive;

        items.Add(ExplorerMenuItemModel.Separator());
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Copy, "Copy", enabled: hasSubject && !isLockedDrive));
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Paste, "Paste", enabled: canPaste));
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.CopyAsPath, "Copy as path", enabled: hasSubject && !isLockedDrive));
        items.Add(ExplorerMenuItemModel.Separator());
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Rename, "Rename", enabled: context.HasTargetPath && !isLockedDrive));
        items.Add(ExplorerMenuItemModel.Separator());
        items.Add(ExplorerMenuItemModel.Command(ExplorerCommandId.Properties, "Properties", enabled: context.HasTargetPath && !isLockedDrive));

        return items;
    }

    private static IReadOnlyList<ExplorerMenuItemModel> BuildBackgroundFolderMenu(ExplorerCommandContext context)
    {
        ExplorerMenuItemModel newFolderItem = ExplorerMenuItemModel.Command(
            ExplorerCommandId.NewFolder,
            "Folder",
            enabled: context.CanCreateFolder);

        return
        [
            ExplorerMenuItemModel.Submenu("New", [newFolderItem]),
            ExplorerMenuItemModel.Separator(),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Paste, "Paste", enabled: context.CanPaste),
            ExplorerMenuItemModel.Separator(),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Refresh, "Refresh", enabled: !string.IsNullOrWhiteSpace(context.WindowId)),
            ExplorerMenuItemModel.Command(ExplorerCommandId.Properties, "Properties", enabled: context.CanShowCurrentLocationProperties)
        ];
    }

    private static IReadOnlyList<ExplorerMenuItemModel> BuildBackgroundThisPcMenu()
    {
        return
        [
            ExplorerMenuItemModel.Command(ExplorerCommandId.Refresh, "Refresh")
        ];
    }

    private static IReadOnlyList<string> GetSubjectPaths(ExplorerCommandContext context)
    {
        if (context.SelectionPaths.Count > 0)
            return context.SelectionPaths;

        if (context.HasTargetPath)
            return [context.TargetPath!];

        return Array.Empty<string>();
    }

    private static string? GetPasteDestination(ExplorerCommandContext context)
    {
        return context.TargetKind switch
        {
            ExplorerCommandTargetKind.BackgroundFolder => context.CurrentLocation,
            ExplorerCommandTargetKind.Folder => context.TargetPath,
            ExplorerCommandTargetKind.Drive => context.TargetPath,
            _ => null
        };
    }

    private static bool HasSubjectPaths(ExplorerCommandContext context)
    {
        return context.SelectionPaths.Count > 0 || context.HasTargetPath;
    }

    private static bool HasDriveIssue(DriveIssueKind? issueKind)
    {
        return issueKind.HasValue && issueKind.Value != DriveIssueKind.None;
    }

    private static bool TryGetBitLockerContextAction(
        bool? isReady,
        bool? isLocked,
        bool? isBitLockerProtected,
        out string menuText,
        out ExplorerBitLockerAction action)
    {
        if (isLocked == true)
        {
            menuText = "Unlock Drive";
            action = ExplorerBitLockerAction.Unlock;
            return true;
        }

        if (isBitLockerProtected == true && isReady != false)
        {
            menuText = "Manage BitLocker";
            action = ExplorerBitLockerAction.Manage;
            return true;
        }

        menuText = string.Empty;
        action = default;
        return false;
    }

    private IReadOnlyList<ExplorerMenuItemModel> BuildExtraFileVerbMenuItems(
    ExplorerCommandContext context)
    {
        if (!context.HasTargetPath)
            return Array.Empty<ExplorerMenuItemModel>();

        ExplorerFileAssociation association =
            _fileAssociationService.ResolveForPath(context.TargetPath!);

        if (association.ExtraVerbs.Count == 0)
            return Array.Empty<ExplorerMenuItemModel>();

        List<ExplorerMenuItemModel> items = [];

        foreach (ExplorerFileVerb verb in association.ExtraVerbs)
        {
            items.Add(ExplorerMenuItemModel.Command(
                ExplorerCommandId.ExtraFileVerb,
                verb.DisplayName,
                enabled: true,
                commandArgument: verb.Id));
        }

        return items;
    }

    private bool TryExecuteExtraFileVerb(string path, string verbId)
    {
        ExplorerFileAssociation association = _fileAssociationService.ResolveForPath(path);

        ExplorerFileVerb? verb = association.ExtraVerbs
            .FirstOrDefault(item => string.Equals(item.Id, verbId, StringComparison.OrdinalIgnoreCase));

        if (verb is null)
            return false;

        _commands.ExecuteFileOpenCommand(path, verb.Command, verb.DisplayName);
        return true;
    }
}
