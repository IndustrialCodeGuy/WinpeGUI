using Explorer.Host.FileOperations.Clipboard;
using Explorer.Host.FileOperations.Delete;
using Explorer.Host.FileOperations.Transfer;
using Explorer.Host.Pickers;
using Explorer.Host.Startup;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shell.Core.FileTypes;
using Shell.Core.Host;
using Shell.Core.Interfaces;
using Shell.Core.Models;
using Shell.Infrastructure.Coordination;
using Shell.Infrastructure.DriveState;
using Shell.Infrastructure.Explorer;
using Shell.Infrastructure.FileTypes;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerIconCache = Explorer.UI.Icons.ExplorerIconCache;
using ShellDialogChrome = Shared.Shell.Utilities.ShellDialogChrome;

namespace Explorer.Host;

internal sealed class ExplorerApplicationContext : ApplicationContext, IExplorerShellCommands, IFileSystemChangeNotifier
{
    private readonly SynchronizationContext _uiContext;

    private readonly BitLockerRuntimeCapabilities _bitLockerCapabilities;
    private readonly bool _canUseDriveDeviceWmiMapping;
    private readonly DriveStateManager _sharedDriveStateManager;
    private readonly DriveStateBuilder _driveStateBuilder;
    private readonly DriveStateStore _driveStateStore;
    private readonly IExplorerDirectoryService _directoryService;
    private readonly ExplorerWindowRegistry _windowRegistry;
    private readonly RefreshCoordinator _refreshCoordinator;
    private readonly StorageChangeCoordinator _storageChangeCoordinator;
    private readonly ExplorerWindowFactory _windowFactory;
    private readonly ExplorerInstanceServer _instanceServer;
    private readonly ExplorerPickerService _pickerService;
    private readonly ExplorerPickerServer _pickerServer;
    private readonly ExplorerIconCache _iconCache;
    private readonly IExplorerCommandService _commandService;
    private readonly ExplorerFileAssociationService _fileAssociations;
    private ExplorerWindowPlacement? _lastBrowseWindowPlacement;
    private int _activeFileOperationCount;
    private bool _idleMemoryCompactionQueued;
    private bool _exitWhenFileOperationsComplete;
    private bool _isExiting;

    public ExplorerApplicationContext(ExplorerLaunchRequest initialRequest, int sessionOwnerProcessId = 0)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("WindowsFormsSynchronizationContext was not available.");

        _bitLockerCapabilities = BitLockerRuntimeCapabilities.Detect();
        _canUseDriveDeviceWmiMapping = DetectDriveDeviceWmiMapping();
        _sharedDriveStateManager = new DriveStateManager(_bitLockerCapabilities);
        _driveStateBuilder = new DriveStateBuilder(_sharedDriveStateManager);
        _driveStateStore = new DriveStateStore(_driveStateBuilder);
        _fileAssociations = new ExplorerFileAssociationService();
        _directoryService = new ExplorerDirectoryService(
            _fileAssociations,
            ExplorerVisibilityOptions.CurrentDefault);

        _windowRegistry = new ExplorerWindowRegistry();
        _refreshCoordinator = new RefreshCoordinator(_driveStateStore, _windowRegistry);
        _storageChangeCoordinator = new StorageChangeCoordinator(_uiContext, _bitLockerCapabilities.IsAvailable);
        _iconCache = new ExplorerIconCache();
        _iconCache.WarmCoreImages(SystemInformation.SmallIconSize.Width);

        _commandService = new ExplorerCommandService(this, _fileAssociations);
        _windowFactory = new ExplorerWindowFactory(
            this,
            _directoryService,
            _commandService,
            _iconCache,
            _fileAssociations);

        _pickerService = new ExplorerPickerService(
            _windowFactory,
            _windowRegistry,
            _driveStateStore,
            _uiContext);

        _sharedDriveStateManager.DriveStatesChanged += SharedDriveStateManager_DriveStatesChanged;
        _storageChangeCoordinator.StorageChanged += StorageChangeCoordinator_StorageChanged;
        _storageChangeCoordinator.Start();

        _instanceServer = new ExplorerInstanceServer(request =>
        {
            _uiContext.Post(_ =>
            {
                if (ShouldOpenWindow(request))
                    OpenWindow(request);
            }, null);
        });
        _instanceServer.Start();

        _pickerServer = new ExplorerPickerServer(ShowPickerAsync);
        _pickerServer.Start();

        _driveStateStore.RefreshAll();

        if (ShouldOpenWindow(initialRequest))
            OpenWindow(initialRequest);

        if (sessionOwnerProcessId > 0)
            Shared.Shell.Utilities.SessionOwnerMonitor.Start(sessionOwnerProcessId, SessionOwnerExited);
    }

    private static bool ShouldOpenWindow(ExplorerLaunchRequest request)
    {
        return !request.HostOnly;
    }

    private void SessionOwnerExited()
    {
        _uiContext.Post(_ =>
        {
            _exitWhenFileOperationsComplete = true;
            ExitIfSessionOwnerGoneAndNoFileOperations();
        }, null);
    }

    private void ExitIfNoShellSurfacesRemain()
    {
        ExitIfSessionOwnerGoneAndNoFileOperations();
    }

    private void ExitIfSessionOwnerGoneAndNoFileOperations()
    {
        if (!_exitWhenFileOperationsComplete ||
            _activeFileOperationCount > 0 ||
            _isExiting)
        {
            return;
        }

        ExitExplorerHost();
    }

    private void ExitExplorerHost()
    {
        if (_isExiting)
            return;

        _isExiting = true;

        foreach (IExplorerWindow window in _windowRegistry.GetAllWindows())
        {
            if (window is not Form form || form.IsDisposed)
                continue;

            try
            {
                form.Close();
            }
            catch
            {
            }
        }

        ExitThread();
    }

    private void BeginFileOperation()
    {
        _activeFileOperationCount++;
    }

    private void CompleteFileOperation()
    {
        _uiContext.Post(_ =>
        {
            if (_activeFileOperationCount > 0)
                _activeFileOperationCount--;

            QueueIdleMemoryCompactionIfIdle();
            ExitIfSessionOwnerGoneAndNoFileOperations();
        }, null);
    }

    private void QueueIdleMemoryCompactionIfIdle()
    {
        if (_isExiting ||
            _activeFileOperationCount > 0 ||
            _windowRegistry.GetAllWindows().Count != 0 ||
            _idleMemoryCompactionQueued)
        {
            return;
        }

        _idleMemoryCompactionQueued = true;

        _uiContext.Post(_ =>
        {
            _idleMemoryCompactionQueued = false;

            if (_isExiting ||
                _activeFileOperationCount > 0 ||
                _windowRegistry.GetAllWindows().Count != 0)
            {
                return;
            }

            CompactManagedMemoryAfterShellSurfacesClosed();
        }, null);
    }

    private const long ForcedIdleCompactionThreshold = 32L * 1024 * 1024;
    private const long AggressiveIdleCompactionThreshold = 64L * 1024 * 1024;

    private static void CompactManagedMemoryAfterShellSurfacesClosed()
    {
        try
        {
            long managedBytes = GC.GetTotalMemory(forceFullCollection: false);

            Debug.WriteLine($"Idle managed memory before compaction: {managedBytes:n0} bytes");

            if (managedBytes < ForcedIdleCompactionThreshold)
                return;

            GCCollectionMode collectionMode =
                managedBytes >= AggressiveIdleCompactionThreshold
                    ? GCCollectionMode.Aggressive
                    : GCCollectionMode.Forced;

            Debug.WriteLine($"Idle GC mode: {collectionMode}");

            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

            GC.Collect(
                GC.MaxGeneration,
                collectionMode,
                blocking: true,
                compacting: true);

            Debug.WriteLine($"Idle managed memory after compaction: {GC.GetTotalMemory(forceFullCollection: false):n0} bytes");
        }
        catch
        {
        }
    }

    public bool CanUseExplorerBitLockerUi => _bitLockerCapabilities.CanUseExplorerBitLockerUi;

    public bool CanEjectDriveDevice(string driveRoot)
    {
        if (!_canUseDriveDeviceWmiMapping || string.IsNullOrWhiteSpace(driveRoot))
            return false;

        try
        {
            string normalizedRoot = DriveStateManager.NormalizeDriveRoot(driveRoot);

            return TryResolveDriveDevice(normalizedRoot, out string pnpDeviceId) &&
                IsDriveDeviceEjectCandidate(pnpDeviceId);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectDriveDeviceWmiMapping()
    {
        try
        {
            using ManagementClass logicalDiskToPartition = new("Win32_LogicalDiskToPartition");
            logicalDiskToPartition.Get();

            using ManagementClass diskDriveToDiskPartition = new("Win32_DiskDriveToDiskPartition");
            diskDriveToDiskPartition.Get();

            using ManagementClass diskDrive = new("Win32_DiskDrive");
            diskDrive.Get();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StorageChangeCoordinator_StorageChanged(object? sender, StorageChangeEventArgs e)
    {
        if (e.Kind == StorageChangeKind.Topology)
        {
            _refreshCoordinator.HandleTopologyChanged(e.Reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(e.DriveRoot))
            _driveStateStore.RequestBitLockerStatesRefresh();
        else
            _driveStateStore.RequestBitLockerStateRefresh(e.DriveRoot);
    }

    private void SharedDriveStateManager_DriveStatesChanged(object? sender, DriveStatesChangedEventArgs e)
    {
        string[] affectedRoots = [.. e.AffectedDriveRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        _uiContext.Post(_ =>
        {
            if (affectedRoots.Length == 1)
            {
                _refreshCoordinator.HandleDriveStateChanged(
                    affectedRoots[0],
                    RefreshReason.BitLockerStateChanged);
                return;
            }

            _refreshCoordinator.HandleDriveStatesChanged(
                affectedRoots,
                RefreshReason.BitLockerStateChanged);
        }, null);
    }

    public void ExecuteFileOpenCommand(string path, ExplorerOpenCommand command, string dialogTitle)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                ExecuteOpenCommand(path, command);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to run command for:\n{path}\n\n{ex.Message}",
                    dialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    public void OpenNewWindow(
        string? initialPath = null,
        ExplorerPreloadedDirectoryListing? preloadedDirectoryListing = null)
    {
        _uiContext.Post(_ =>
        {
            ExplorerWindowOptions options = new()
            {
                InitialPath = initialPath,
                Mode = ExplorerWindowMode.Browse,
                PreloadedDirectoryListing = preloadedDirectoryListing
            };

            OpenWindow(options);
        }, null);
    }

    public void RefreshWindow(string windowId)
    {
        _uiContext.Post(_ => _refreshCoordinator.HandleManualRefresh(windowId), null);
    }

    public void NotifyFileChanged(string parentFolderPath, RefreshReason reason)
    {
        DispatchFileSystemChange(() => _refreshCoordinator.HandleFileChanged(parentFolderPath, reason));
    }

    public void NotifyFolderChildrenChanged(string parentFolderPath, RefreshReason reason)
    {
        DispatchFileSystemChange(() => _refreshCoordinator.HandleFolderChildrenChanged(parentFolderPath, reason));
    }

    public void NotifyFolderRelocated(string oldPath, string newPath, RefreshReason reason)
    {
        DispatchFileSystemChange(() => _refreshCoordinator.HandleFolderRelocated(oldPath, newPath, reason));
    }

    public void NotifyFolderDeleted(string deletedFolderPath, RefreshReason reason)
    {
        DispatchFileSystemChange(() => _refreshCoordinator.HandleFolderDeleted(deletedFolderPath, reason));
    }

    private Task<ExplorerPickerResult> ShowPickerAsync(
        ExplorerPickerRequest request,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<ExplorerPickerResult> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _uiContext.Post(_ =>
        {
            try
            {
                completion.SetResult(_pickerService.ShowPicker(request, GetDialogOwner(), cancellationToken));
            }
            catch (Exception ex)
            {
                completion.SetResult(ExplorerPickerResult.Error(ex.Message));
            }
            finally
            {
                QueueIdleMemoryCompactionIfIdle();
            }
        }, null);

        return completion.Task;
    }

    public void OpenWindow(ExplorerLaunchRequest request)
    {
        OpenWindow(request.ToWindowOptions());
    }

    private void OpenWindow(ExplorerWindowOptions options)
    {
        if (_isExiting)
            return;
        if (options.Mode == ExplorerWindowMode.Browse && _lastBrowseWindowPlacement is not null)
        {
            options = new ExplorerWindowOptions
            {
                InitialPath = options.InitialPath,
                Mode = options.Mode,
                Title = options.Title,
                AllowedExtensions = options.AllowedExtensions,
                Placement = _lastBrowseWindowPlacement,
                PreloadedDirectoryListing = options.PreloadedDirectoryListing
            };
        }

        IExplorerWindow window = _windowFactory.CreateWindow(options);
        _windowRegistry.Register(window);

        if (window is Form form)
        {
            form.FormClosed += (_, _) =>
            {
                ExplorerWindowPlacement? placement = window.GetWindowPlacement();
                if (placement is not null)
                    _lastBrowseWindowPlacement = placement;

                _windowRegistry.Unregister(window.WindowId);
                QueueIdleMemoryCompactionIfIdle();
                ExitIfNoShellSurfacesRemain();
            };

            window.ActivateWindow();
        }

        window.ApplyDriveSetSnapshot(
            _driveStateStore.GetCurrentSnapshot(),
            RefreshReason.InternalRequest);

        window.RequestRefreshCurrentView(RefreshReason.InternalRequest);
    }

    public void OpenFileSystemItem(string path)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                if (Directory.Exists(path))
                {
                    OpenWindow(new ExplorerLaunchRequest
                    {
                        InitialPath = path
                    });
                    return;
                }

                ExplorerFileAssociation association = _fileAssociations.ResolveForPath(path);
                if (association.DefaultOpenCommand is null)
                {
                    OpenItemWith(path);
                    return;
                }

                ExecuteOpenCommand(path, association.DefaultOpenCommand);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open:\n{path}\n\n{ex.Message}",
                    "Open Item",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    public void CopyPathsToClipboard(IReadOnlyList<string> paths)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                if (paths == null || paths.Count == 0)
                    return;

                string text = string.Join(
                    Environment.NewLine,
                    paths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => $"\"{path}\""));

                if (string.IsNullOrWhiteSpace(text))
                    return;

                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to copy path.\n\n{ex.Message}",
                    "Copy as Path",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    private static void ExecuteOpenCommand(string path, ExplorerOpenCommand command)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string executablePath = ExpandCommandPath(command.ExecutablePath, path);
        string arguments = BuildCommandArguments(command.Arguments, path);

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false
        };

        string? workingDirectory = TryGetLaunchWorkingDirectory(path);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        using Process? process = Process.Start(startInfo);
    }

    private static string ExpandCommandPath(string executablePath, string targetPath)
    {
        if (string.Equals(executablePath, "%1", StringComparison.OrdinalIgnoreCase))
            return targetPath;

        return Environment.ExpandEnvironmentVariables(executablePath);
    }

    private static string BuildCommandArguments(string arguments, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return string.Empty;

        return Environment.ExpandEnvironmentVariables(arguments)
            .Replace("%1", targetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%L", targetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%*", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetLaunchWorkingDirectory(string targetPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return null;

            string? directory = Directory.Exists(targetPath)
                ? targetPath
                : Path.GetDirectoryName(targetPath);

            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void OpenItemProperties(string path)
    {
        ExecuteShellVerbOnUiThread(path, "properties", "Properties");
    }

    public void OpenItemWith(string path)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                using OpenWithCommandDialog dialog = new(BrowseForOpenWithProgram);

                if (dialog.ShowDialog(GetDialogOwner()) != DialogResult.OK)
                    return;

                ExecuteOpenWithCommand(path, dialog.CommandLine);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to open with command for:\n{path}\n\n{ex.Message}",
                    "Open With",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    public void EditFileInNotepad(string path)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to edit in Notepad:\n{path}\n\n{ex.Message}",
                    "Edit in Notepad",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    public void FormatDrive(string driveRoot)
    {
        ExecuteShellVerbOnUiThread(driveRoot, "format", "Format");
    }

    public void EjectOrDisconnectDrive(string driveRoot)
    {
        _uiContext.Post(_ =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(driveRoot))
                    return;

                string normalizedRoot = DriveStateManager.NormalizeDriveRoot(driveRoot);
                DriveType driveType = DriveType.Unknown;

                try
                {
                    driveType = new DriveInfo(normalizedRoot).DriveType;
                }
                catch
                {
                }

                if (driveType == DriveType.Network)
                {
                    DisconnectMappedNetworkDrive(normalizedRoot);
                    return;
                }

                if (driveType == DriveType.CDRom)
                {
                    if (ExecuteShellVerbForPath(normalizedRoot, "eject", "Eject"))
                    {
                        _refreshCoordinator.HandleTopologyChanged(RefreshReason.DeviceRemoval);
                    }

                    return;
                }

                if (TryEjectDriveDevice(normalizedRoot))
                {
                    _refreshCoordinator.HandleTopologyChanged(RefreshReason.DeviceRemoval);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Unable to eject or disconnect:\n{driveRoot}\n\n{ex.Message}",
                    "Eject",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }, null);
    }

    private static void ExecuteOpenWithCommand(string path, string commandLine)
    {
        if (!TryBuildOpenWithStartInfo(
            path,
            commandLine,
            out ProcessStartInfo? startInfo,
            out string? errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        using Process? process = Process.Start(startInfo);
    }

    private static bool TryBuildOpenWithStartInfo(
        string targetPath,
        string commandLine,
        out ProcessStartInfo? startInfo,
        out string? errorMessage)
    {
        startInfo = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            errorMessage = "A target file path is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commandLine))
        {
            errorMessage = "A command is required.";
            return false;
        }

        string expandedCommandLine = Environment.ExpandEnvironmentVariables(commandLine.Trim());

        if (!TrySplitCommandLine(
            expandedCommandLine,
            out string executablePath,
            out string arguments,
            out errorMessage))
        {
            return false;
        }

        bool commandContainsTarget =
            ContainsTargetToken(executablePath) ||
            ContainsTargetToken(arguments);

        executablePath = ReplaceOpenWithTargetTokens(executablePath, targetPath).Trim();

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            errorMessage = "A program path is required.";
            return false;
        }

        arguments = ReplaceOpenWithTargetTokens(arguments, targetPath).Trim();

        if (!commandContainsTarget)
            arguments = AppendQuotedArgument(arguments, targetPath);

        startInfo = new ProcessStartInfo
        {
            FileName = executablePath.Trim('"'),
            Arguments = arguments,
            UseShellExecute = true
        };

        return true;
    }

    private string? BrowseForOpenWithProgram(IWin32Window owner)
    {
        ExplorerPickerRequest request = new()
        {
            Mode = ExplorerWindowMode.OpenFile,
            InitialPath = Environment.GetFolderPath(Environment.SpecialFolder.System),
            Title = "Choose a program",
            OwnerWindowHandle = owner.Handle.ToInt64(),
            AllowedExtensions = [".exe", ".com", ".bat", ".cmd"]
        };

        ExplorerPickerResult result = _pickerService.ShowPicker(
            request,
            owner,
            CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            MessageBox.Show(
                owner,
                result.ErrorMessage,
                "Choose a program",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return null;
        }

        return result.Accepted ? result.SelectedPath : null;
    }

    private static bool TrySplitCommandLine(
        string commandLine,
        out string executablePath,
        out string arguments,
        out string? errorMessage)
    {
        executablePath = string.Empty;
        arguments = string.Empty;
        errorMessage = null;

        string trimmed = commandLine.Trim();

        if (trimmed.Length == 0)
        {
            errorMessage = "A command is required.";
            return false;
        }

        if (trimmed[0] == '"')
        {
            int closingQuoteIndex = trimmed.IndexOf('"', 1);

            if (closingQuoteIndex < 0)
            {
                errorMessage = "The command contains an opening quote without a closing quote.";
                return false;
            }

            executablePath = trimmed[1..closingQuoteIndex];
            arguments = trimmed[(closingQuoteIndex + 1)..].Trim();
            return true;
        }

        int firstSpaceIndex = trimmed.IndexOfAny([' ', '\t']);

        if (firstSpaceIndex < 0)
        {
            executablePath = trimmed;
            arguments = string.Empty;
            return true;
        }

        executablePath = trimmed[..firstSpaceIndex];
        arguments = trimmed[(firstSpaceIndex + 1)..].Trim();
        return true;
    }

    private static bool ContainsTargetToken(string value)
    {
        return value.Contains("%1", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("%L", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplaceOpenWithTargetTokens(string value, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value
            .Replace("%1", targetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%L", targetPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%*", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendQuotedArgument(string arguments, string targetPath)
    {
        string quotedTargetPath = $"\"{targetPath}\"";

        if (string.IsNullOrWhiteSpace(arguments))
            return quotedTargetPath;

        return $"{arguments} {quotedTargetPath}";
    }

    private void ExecuteShellVerbOnUiThread(string path, string verb, string caption)
    {
        _uiContext.Post(_ =>
        {
            ExecuteShellVerbForPath(path, verb, caption);
        }, null);
    }

    private static bool ExecuteShellVerbForPath(string path, string verb, string caption)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            SHELLEXECUTEINFO info = new()
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_FLAG_NO_UI,
                hwnd = Form.ActiveForm?.Handle ?? IntPtr.Zero,
                lpVerb = verb,
                lpFile = path,
                nShow = 5
            };

            bool ok = ShellExecuteEx(ref info);
            if (ok)
                return true;

            int error = Marshal.GetLastWin32Error();
            if (error != 0)
                throw new Win32Exception(error);

            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                caption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }
    }

    private bool DisconnectMappedNetworkDrive(string pathOrRoot)
    {
        string root = DriveStateManager.NormalizeDriveRoot(pathOrRoot);
        string localName = root.TrimEnd('\\');

        int result = WNetCancelConnection2W(localName, CONNECT_UPDATE_PROFILE, false);
        if (result != NO_ERROR)
        {
            MessageBox.Show(
                new Win32Exception(result).Message,
                "Disconnect",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        _refreshCoordinator.HandleTopologyChanged(RefreshReason.DeviceRemoval);
        return true;
    }

    private static bool TryEjectDriveDevice(string pathOrRoot)
    {
        string targetRoot = DriveStateManager.NormalizeDriveRoot(pathOrRoot);
        if (!TryResolveDriveDevice(targetRoot, out string pnpDeviceId) ||
            !IsDriveDeviceEjectCandidate(pnpDeviceId))
        {
            return false;
        }

        return TryRequestDeviceEject(pnpDeviceId);
    }

    private static bool TryResolveDriveDevice(
        string targetRoot,
        out string pnpDeviceId)
    {
        string targetLogicalDeviceId = targetRoot.TrimEnd('\\');

        pnpDeviceId = string.Empty;

        try
        {
            Dictionary<string, string> partitionByLogicalDisk =
                new(StringComparer.OrdinalIgnoreCase);

            using (ManagementObjectSearcher logicalToPartition =
                   new("SELECT * FROM Win32_LogicalDiskToPartition"))
            using (ManagementObjectCollection associations = logicalToPartition.Get())
            {
                foreach (ManagementObject association in associations.Cast<ManagementObject>())
                {
                    using (association)
                    {
                        string? logicalDeviceId =
                            TryGetWmiDeviceId(association["Dependent"] as string);

                        string? partitionDeviceId =
                            TryGetWmiDeviceId(association["Antecedent"] as string);

                        if (!string.IsNullOrWhiteSpace(logicalDeviceId) &&
                            !string.IsNullOrWhiteSpace(partitionDeviceId))
                        {
                            partitionByLogicalDisk[logicalDeviceId] = partitionDeviceId;
                        }
                    }
                }
            }

            if (!partitionByLogicalDisk.TryGetValue(targetLogicalDeviceId, out string? targetPartition))
                return false;

            Dictionary<string, string> diskObjectPathByPartition =
                new(StringComparer.OrdinalIgnoreCase);

            using (ManagementObjectSearcher diskToPartition =
                   new("SELECT * FROM Win32_DiskDriveToDiskPartition"))
            using (ManagementObjectCollection associations = diskToPartition.Get())
            {
                foreach (ManagementObject association in associations.Cast<ManagementObject>())
                {
                    using (association)
                    {
                        string? diskObjectPath = association["Antecedent"] as string;
                        string? partitionDeviceId =
                            TryGetWmiDeviceId(association["Dependent"] as string);

                        if (!string.IsNullOrWhiteSpace(diskObjectPath) &&
                            !string.IsNullOrWhiteSpace(partitionDeviceId))
                        {
                            diskObjectPathByPartition[partitionDeviceId] = diskObjectPath;
                        }
                    }
                }
            }

            if (!diskObjectPathByPartition.TryGetValue(targetPartition, out string? targetDiskObjectPath))
                return false;

            using (ManagementObject disk = new(targetDiskObjectPath))
            {
                disk.Get();

                pnpDeviceId = disk["PNPDeviceID"] as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pnpDeviceId))
                    return false;
            }

            return true;
        }
        catch
        {
            pnpDeviceId = string.Empty;
            return false;
        }
    }

    private static string? TryGetWmiDeviceId(string? objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
            return null;

        try
        {
            using ManagementObject instance = new(objectPath);
            instance.Get();
            return instance["DeviceID"] as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsDriveDeviceEjectCandidate(string pnpDeviceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out uint devInst, pnpDeviceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                return false;

            return IsDriveDeviceEjectCandidate(devInst);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDriveDeviceEjectCandidate(uint devInst)
    {
        uint currentDevInst = devInst;
        HashSet<uint> visitedDevInsts = [];

        while (visitedDevInsts.Add(currentDevInst))
        {
            if (HasEjectableStorageInstanceId(currentDevInst))
                return true;

            if (CM_Get_Parent(out uint parentDevInst, currentDevInst, 0) != CR_SUCCESS ||
                parentDevInst == currentDevInst)
            {
                return false;
            }

            if (!TryGetDeviceInstanceId(parentDevInst, out string parentDeviceId) ||
                IsHardEjectParentBoundary(parentDeviceId))
            {
                return false;
            }

            currentDevInst = parentDevInst;
        }

        return false;
    }

    private static bool HasEjectableStorageInstanceId(uint devInst)
    {
        return TryGetDeviceInstanceId(devInst, out string instanceId) &&
            IsEjectableStorageInstanceId(instanceId);
    }

    private static bool IsEjectableStorageInstanceId(string instanceId)
    {
        if (instanceId.StartsWith(@"USBSTOR\", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"UASPSTOR\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase) &&
            !instanceId.StartsWith(@"USB\ROOT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRequestDeviceEject(string pnpDeviceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out uint devInst, pnpDeviceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                return false;

            uint currentDevInst = devInst;
            HashSet<uint> visitedDevInsts = [];
            PnpVetoType lastVetoType = PnpVetoType.TypeUnknown;
            string lastVetoName = string.Empty;

            while (visitedDevInsts.Add(currentDevInst))
            {
                if (TryRequestDeviceEject(currentDevInst, out lastVetoType, out lastVetoName))
                    return true;

                if (CM_Get_Parent(out uint parentDevInst, currentDevInst, 0) != CR_SUCCESS)
                {
                    ShowEjectFailureMessage(lastVetoType, lastVetoName);
                    return false;
                }

                if (parentDevInst == currentDevInst)
                {
                    ShowEjectFailureMessage(lastVetoType, lastVetoName);
                    return false;
                }

                if (!TryGetDeviceInstanceId(parentDevInst, out string parentDeviceId) ||
                    IsHardEjectParentBoundary(parentDeviceId))
                {
                    ShowEjectFailureMessage(lastVetoType, lastVetoName);
                    return false;
                }

                currentDevInst = parentDevInst;
            }

            ShowEjectFailureMessage(lastVetoType, lastVetoName);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDeviceInstanceId(uint devInst, out string instanceId)
    {
        StringBuilder buffer = new(MaxDeviceInstanceIdLength);

        if (CM_Get_Device_IDW(devInst, buffer, buffer.Capacity, 0) != CR_SUCCESS)
        {
            instanceId = string.Empty;
            return false;
        }

        instanceId = buffer.ToString();
        return !string.IsNullOrWhiteSpace(instanceId);
    }

    private static bool IsHardEjectParentBoundary(string instanceId)
    {
        return instanceId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) ||
            instanceId.StartsWith(@"USB\ROOT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRequestDeviceEject(
        uint devInst,
        out PnpVetoType vetoType,
        out string vetoName)
    {
        try
        {
            StringBuilder vetoBuilder = new(260);

            int cr = CM_Request_Device_EjectW(
                devInst,
                out vetoType,
                vetoBuilder,
                vetoBuilder.Capacity,
                0);

            vetoName = vetoBuilder.ToString();

            Debug.WriteLine(
                $"CM_Request_Device_EjectW devInst={devInst} cr={cr} vetoType={vetoType} vetoName='{vetoName}'");

            return cr == CR_SUCCESS;
        }
        catch
        {
            vetoType = PnpVetoType.TypeUnknown;
            vetoName = string.Empty;
            return false;
        }
    }

    private static void ShowEjectFailureMessage(PnpVetoType vetoType, string vetoName)
    {
        string message = vetoType switch
        {
            PnpVetoType.PendingClose or
            PnpVetoType.OutstandingOpen or
            PnpVetoType.WindowsApp or
            PnpVetoType.WindowsService
                => "The device is currently in use. Close any programs or windows that might be using the device, and then try again.",

            _ => "Windows can't stop the device right now. Try again after closing any files or programs that might be using it."
        };

        if (!string.IsNullOrWhiteSpace(vetoName))
            message += $"\r\n\r\nIn use by: {vetoName}";

        MessageBox.Show(
            message,
            "Eject",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    public string? CreateNewFolder(string parentFolderPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(parentFolderPath) || !Directory.Exists(parentFolderPath))
                return null;

            string basePath = Path.Combine(parentFolderPath, "New Folder");
            string newFolderPath = GetUniqueNewFolderPath(basePath);

            Directory.CreateDirectory(newFolderPath);
            NotifyFolderChildrenChanged(parentFolderPath, RefreshReason.InternalRequest);

            return newFolderPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "New Folder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return null;
        }
    }

    private static string GetUniqueNewFolderPath(string basePath)
    {
        if (!Directory.Exists(basePath) && !File.Exists(basePath))
            return basePath;

        string parent = Path.GetDirectoryName(basePath) ?? string.Empty;
        string leaf = Path.GetFileName(basePath);

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(parent, $"{leaf} ({i})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }
    }

    public void ShowOpticalDriveEmptyMessage(string driveRoot)
    {
        string normalizedRoot = string.IsNullOrWhiteSpace(driveRoot)
            ? "the selected drive"
            : DriveStateManager.NormalizeDriveRoot(driveRoot).TrimEnd('\\');

        MessageBox.Show(
            $"Insert a disk into disk drive ({normalizedRoot}).",
            "Disk Drive",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public void ShowDriveNotReadyMessage(
        string driveRoot,
        DriveIssueKind? issueKind = null,
        string? issueMessage = null)
    {
        string normalizedRoot = string.IsNullOrWhiteSpace(driveRoot)
            ? "the selected drive"
            : DriveStateManager.NormalizeDriveRoot(driveRoot).TrimEnd('\\');

        DriveIssueKind resolvedIssueKind = ResolveDriveIssueKind(driveRoot, issueKind);
        (string title, string message) = BuildDriveIssueMessage(normalizedRoot, resolvedIssueKind, issueMessage);

        MessageBox.Show(
            message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private DriveIssueKind ResolveDriveIssueKind(string driveRoot, DriveIssueKind? issueKind)
    {
        if (issueKind.HasValue && issueKind.Value != DriveIssueKind.None)
            return issueKind.Value;

        if (!string.IsNullOrWhiteSpace(driveRoot) &&
            _driveStateStore.TryGetDrive(driveRoot) is { } snapshot)
        {
            if (snapshot.IsEffectivelyBitLockerLocked)
                return DriveIssueKind.BitLockerLocked;

            if (snapshot.IssueKind != DriveIssueKind.None)
                return snapshot.IssueKind;
        }

        return DriveIssueKind.NotReady;
    }

    private (string Title, string Message) BuildDriveIssueMessage(
        string normalizedRoot,
        DriveIssueKind issueKind,
        string? issueMessage)
    {
        return issueKind switch
        {
            DriveIssueKind.BitLockerLocked =>
                ("BitLocker", BuildBitLockerLockedMessage(normalizedRoot)),

            DriveIssueKind.OpticalNoMedia =>
                ("Disk Drive", $"Insert a disk into disk drive ({normalizedRoot})."),

            DriveIssueKind.UnrecognizedVolume =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nThe volume is unrecognized, unformatted, or uses a file system that is not available in this environment."),

            DriveIssueKind.DeviceNotConnected =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nThe device is no longer connected or is not responding."),

            DriveIssueKind.AccessDenied =>
                ("Drive Not Accessible", $"The drive is not accessible:\n{normalizedRoot}\n\nAccess was denied."),

            DriveIssueKind.NotReady or DriveIssueKind.RemovableNoMediaOrUnavailable =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nInsert media or check that the device is connected."),

            DriveIssueKind.BitLockerStatusUnavailableNotElevated =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nRun File Manager as administrator to check or unlock BitLocker-protected drives."),

            DriveIssueKind.BitLockerStatusProviderUnavailable =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nThis WinPE image is missing the required BitLocker/SecureStartup components to unlock it here."),

            DriveIssueKind.BitLockerStatusCheckFailed =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nBitLocker status could not be checked in this environment."),

            _ =>
                ("Drive Not Ready", $"The drive is not ready:\n{normalizedRoot}\n\nThe device may be disconnected, unavailable, unformatted, or reporting an I/O error.")
        };
    }

    private string BuildBitLockerLockedMessage(string normalizedRoot)
    {
        string detail = _bitLockerCapabilities.State switch
        {
            BitLockerIntegrationState.NotElevated =>
                "Run File Manager as administrator to check or unlock BitLocker-protected drives.",

            BitLockerIntegrationState.ProviderUnavailable =>
                "This WinPE image is missing the required BitLocker/SecureStartup components to unlock it here.",

            _ =>
                "Unlock the drive to access its contents."
        };

        return $"The drive appears to be locked by BitLocker:\n{normalizedRoot}\n\n{detail}";
    }

    public void LaunchBitLockerHelper(
        string driveRoot,
        ExplorerBitLockerAction action,
        string? navigateAfterUnlockPath = null,
        string? navigationWindowId = null,
        bool openInNewWindowAfterUnlock = false)
    {
        if (!_bitLockerCapabilities.CanUseExplorerBitLockerUi)
        {
            ShowDriveNotReadyMessage(driveRoot);
            return;
        }

        string fileName = action == ExplorerBitLockerAction.Unlock
            ? "BitLocker.Unlock.exe"
            : "BitLocker.Manager.exe";

        string helperPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(helperPath))
        {
            MessageBox.Show(
                $"BitLocker helper was not found:\n{helperPath}",
                "BitLocker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = helperPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        };

        if (action is ExplorerBitLockerAction.Unlock or ExplorerBitLockerAction.Manage)
        {
            startInfo.ArgumentList.Add("--drive");
            startInfo.ArgumentList.Add(driveRoot);
        }

        if (ShellTheme.DarkMode)
            startInfo.ArgumentList.Add("--dark");

        try
        {
            Process? process = Process.Start(startInfo);

            if (action == ExplorerBitLockerAction.Unlock &&
                process != null &&
                !string.IsNullOrWhiteSpace(navigateAfterUnlockPath))
            {
                _ = FollowBitLockerUnlockAsync(
                    process,
                    navigateAfterUnlockPath,
                    navigationWindowId,
                    openInNewWindowAfterUnlock);
            }
            else
            {
                process?.Dispose();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to start BitLocker helper:\n{ex.Message}",
                "BitLocker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task FollowBitLockerUnlockAsync(
        Process process,
        string navigateAfterUnlockPath,
        string? navigationWindowId,
        bool openInNewWindowAfterUnlock)
    {
        int exitCode;

        try
        {
            using (process)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
        }
        catch
        {
            return;
        }

        if (exitCode != 0)
            return;

        _uiContext.Post(_ => CompleteBitLockerUnlockNavigation(
            navigateAfterUnlockPath,
            navigationWindowId,
            openInNewWindowAfterUnlock), null);
    }

    private void CompleteBitLockerUnlockNavigation(
        string navigateAfterUnlockPath,
        string? navigationWindowId,
        bool openInNewWindowAfterUnlock)
    {
        string requestedPath = navigateAfterUnlockPath.Trim();
        if (string.IsNullOrWhiteSpace(requestedPath))
            return;

        string rootPath = Path.GetPathRoot(requestedPath) ?? requestedPath;
        if (!Directory.Exists(rootPath))
            return;

        string targetPath = Directory.Exists(requestedPath)
            ? requestedPath
            : rootPath;

        if (openInNewWindowAfterUnlock)
        {
            OpenNewWindow(targetPath);
            return;
        }

        if (string.IsNullOrWhiteSpace(navigationWindowId))
            return;

        IExplorerWindow? window = _windowRegistry
            .GetAllWindows()
            .FirstOrDefault(w => string.Equals(w.WindowId, navigationWindowId, StringComparison.Ordinal));

        if (window == null)
            return;

        window.NavigateToPath(targetPath);
        window.ActivateWindow();
    }

    public bool RenameFileSystemEntry(string path, bool isDirectory, string newName)
    {
        string currentName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(currentName))
            return false;

        newName = (newName ?? string.Empty).Trim();

        if (string.Equals(newName, currentName, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(
                "The name cannot be empty.",
                "Rename",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(
                "The name contains invalid characters.",
                "Rename",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        string? parentPath = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(parentPath))
            return false;

        string newPath = Path.Combine(parentPath, newName);

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            MessageBox.Show(
                "An item with that name already exists.",
                "Rename",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            if (isDirectory)
                Directory.Move(path, newPath);
            else
                File.Move(path, newPath);

            if (isDirectory)
                NotifyFolderRelocated(path, newPath, RefreshReason.InternalRequest);
            else
                NotifyFileChanged(parentPath, RefreshReason.InternalRequest);

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Rename",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    public bool RenameDriveLabel(string rootPath, string newLabel)
    {
        newLabel = (newLabel ?? string.Empty).Trim();

        if (ContainsInvalidVolumeLabelChars(newLabel))
        {
            MessageBox.Show(
                "The drive label contains invalid characters.",
                "Rename Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        string currentLabel = GetCurrentDriveLabel(rootPath);

        if (string.Equals(newLabel, currentLabel, StringComparison.Ordinal))
            return false;

        try
        {
            if (!SetVolumeLabel(rootPath, string.IsNullOrWhiteSpace(newLabel) ? null : newLabel))
            {
                int error = Marshal.GetLastWin32Error();
                MessageBox.Show(
                    $"Unable to rename the drive label. Win32 error: {error}",
                    "Rename Drive",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            _driveStateStore.RefreshDrive(rootPath);
            _refreshCoordinator.HandleDriveStateChanged(rootPath, RefreshReason.InternalRequest);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Rename Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    public void DeletePaths(IReadOnlyList<string> paths)
    {
        _uiContext.Post(_ =>
        {
            string[] deletePaths = (paths ?? Array.Empty<string>())
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (deletePaths.Length == 0)
                return;

            Form? owner = GetDialogOwner();

            DeleteProgressForm progressForm = new(
                _fileAssociations,
                path => OpenNewWindow(path));
            _ = progressForm.Handle;

            bool deleteStarted = false;

            progressForm.FormClosed += (_, _) =>
            {
                if (deleteStarted)
                    return;

                try
                {
                    if (!progressForm.IsDisposed)
                        progressForm.Dispose();
                }
                catch
                {
                }

                RestoreDialogOwner(owner);
            };

            progressForm.ShowDeleteConfirmation(owner, deletePaths, () =>
            {
                deleteStarted = true;
                BeginFileOperation();

                Task.Run(() =>
                {
                    try
                    {
                        ExplorerDeleteEngine.ExecuteDelete(
                            deletePaths,
                            progressForm,
                            this,
                            RefreshReason.InternalRequest);

                        CloseDeleteProgressForm(progressForm);
                    }
                    catch (Exception ex)
                    {
                        CloseDeleteProgressForm(progressForm, ex.Message);
                    }
                    finally
                    {
                        CompleteFileOperation();
                    }
                });
            });
        }, null);
    }

    public void SetClipboardFileTransfer(IReadOnlyList<string> sourcePaths, bool move)
    {
        if (sourcePaths is null || sourcePaths.Count == 0)
            return;

        if (SynchronizationContext.Current == _uiContext)
        {
            ExplorerClipboardTransferService.SetFileTransfer(sourcePaths, move);
            return;
        }

        _uiContext.Send(_ => ExplorerClipboardTransferService.SetFileTransfer(sourcePaths, move), null);
    }

    public bool ClearClipboard()
    {
        if (SynchronizationContext.Current == _uiContext)
            return ExplorerClipboardTransferService.TryClear();

        bool cleared = false;
        _uiContext.Send(_ =>
        {
            cleared = ExplorerClipboardTransferService.TryClear();
        }, null);

        return cleared;
    }

    public bool CanPasteFileTransfer()
    {
        if (SynchronizationContext.Current == _uiContext)
        {
            return ExplorerClipboardTransferService.TryGetFileTransfer(out ExplorerTransferManifest? manifest);
        }

        bool canPaste = false;
        _uiContext.Send(_ =>
        {
            canPaste = ExplorerClipboardTransferService.TryGetFileTransfer(out ExplorerTransferManifest? manifest);
        }, null);

        return canPaste;
    }

    public void PasteFileTransfer(string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            return;

        _uiContext.Post(_ =>
        {
            if (!Directory.Exists(destinationFolder))
                return;

            if (!ExplorerClipboardTransferService.TryGetFileTransfer(out ExplorerTransferManifest? manifest) ||
                manifest is null ||
                manifest.SourcePaths.Count == 0)
            {
                return;
            }

            Form? owner = GetDialogOwner();
            TransferProgressForm progressForm = new(
                manifest.Move,
                _fileAssociations,
                path => OpenNewWindow(path));
            progressForm.StartDeferredShow(owner, delayMs: 200);
            BeginFileOperation();

            Task.Run(() =>
            {
                try
                {
                    ExplorerTransferEngine.ExecuteTransfer(
                        destinationFolder,
                        manifest,
                        progressForm,
                        this,
                        RefreshReason.InternalRequest);

                    CloseTransferProgressForm(progressForm);
                }
                catch (Exception ex)
                {
                    CloseTransferProgressForm(progressForm, ex.Message, manifest.Move ? "Move" : "Copy");
                }
                finally
                {
                    CompleteFileOperation();
                }
            });
        }, null);
    }

    private void CloseTransferProgressForm(
        TransferProgressForm progressForm,
        string? errorMessage = null,
        string operationCaption = "Copy")
    {
        _uiContext.Post(_ =>
        {
            Form? restoreOwner = ShellDialogChrome.GetRestoreOwner(progressForm);

            ShellDialogChrome.SafeCloseAndDispose(progressForm);

            if (!string.IsNullOrWhiteSpace(errorMessage))
                ShellDialogChrome.ShowError(restoreOwner, errorMessage, operationCaption);

            RestoreDialogOwner(restoreOwner);
        }, null);
    }

    private static void RestoreDialogOwner(Form? owner)
    {
        ShellDialogChrome.RestoreOwner(owner);
    }

    private static Form? GetDialogOwner()
    {
        return ShellDialogChrome.GetDialogOwner();
    }

    private void CloseDeleteProgressForm(DeleteProgressForm progressForm, string? errorMessage = null)
    {
        _uiContext.Post(_ =>
        {
            Form? restoreOwner = ShellDialogChrome.GetRestoreOwner(progressForm);

            ShellDialogChrome.SafeCloseAndDispose(progressForm);

            if (!string.IsNullOrWhiteSpace(errorMessage))
                ShellDialogChrome.ShowError(restoreOwner, errorMessage, "Delete");

            RestoreDialogOwner(restoreOwner);
        }, null);
    }

    private static string GetCurrentDriveLabel(string rootPath)
    {
        try
        {
            DriveInfo drive = new(rootPath);
            if (drive.IsReady)
                return drive.VolumeLabel ?? string.Empty;
        }
        catch
        {
        }

        return string.Empty;
    }

    private static bool ContainsInvalidVolumeLabelChars(string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        foreach (char c in label)
        {
            switch (c)
            {
                case '<':
                case '>':
                case ':':
                case '"':
                case '/':
                case '\\':
                case '|':
                case '?':
                case '*':
                    return true;
            }
        }

        return false;
    }

    private void DispatchFileSystemChange(Action action)
    {
        if (SynchronizationContext.Current == _uiContext)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetVolumeLabel(string lpRootPathName, string? lpVolumeName);

    private const int SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SEE_MASK_INVOKEIDLIST = 0x0000000C;

    private const int NO_ERROR = 0;
    private const int CONNECT_UPDATE_PROFILE = 0x00000001;

    private const int CR_SUCCESS = 0;
    private const int CM_LOCATE_DEVNODE_NORMAL = 0;
    private const int MaxDeviceInstanceIdLength = 200;

    private enum PnpVetoType
    {
        TypeUnknown = 0,
        LegacyDevice = 1,
        PendingClose = 2,
        WindowsApp = 3,
        WindowsService = 4,
        OutstandingOpen = 5,
        Device = 6,
        Driver = 7,
        IllegalDeviceRequest = 8,
        InsufficientPower = 9,
        NonDisableable = 10,
        LegacyDriver = 11,
        InsufficientRights = 12
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Request_Device_EjectW(
        uint dnDevInst,
        out PnpVetoType pVetoType,
        StringBuilder? pszVetoName,
        int ulNameLength,
        int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(
        out uint pdnDevInst,
        uint dnDevInst,
        int ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(
        uint dnDevInst,
        StringBuilder buffer,
        int bufferLen,
        int ulFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2W(
        string lpName,
        int dwFlags,
        [MarshalAs(UnmanagedType.Bool)] bool fForce);

    protected override void ExitThreadCore()
    {
        _isExiting = true;

        _sharedDriveStateManager.DriveStatesChanged -= SharedDriveStateManager_DriveStatesChanged;
        _storageChangeCoordinator.StorageChanged -= StorageChangeCoordinator_StorageChanged;
        _pickerServer.Dispose();
        _instanceServer.Dispose();
        _storageChangeCoordinator.Dispose();
        _iconCache.Dispose();
        base.ExitThreadCore();
    }
}
