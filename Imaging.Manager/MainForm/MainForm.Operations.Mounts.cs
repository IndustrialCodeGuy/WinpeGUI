using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task MountWimAsync()
    {
        if (_operationActive)
            return;

        string? imagePath = RunExplorerPicker(
            save: false,
            title: "Select WIM to mount",
            extension: ".wim",
            initialPath: GetSelectedOpticalVolume()?.MountPoint);
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Mount WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        WimImageInfoResult? imageInfo = await TryLoadWimImageInfoAsync(imagePath, "Mount WIM");
        if (imageInfo == null)
            return;

        string? mountDirectory = RunExplorerFolderPicker("Select empty folder for WIM mount");
        if (string.IsNullOrWhiteSpace(mountDirectory))
            return;

        string imageFullPath;
        string mountFullPath;
        try
        {
            imageFullPath = Path.GetFullPath(imagePath);
            mountFullPath = Path.GetFullPath(mountDirectory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Mount WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!Directory.Exists(mountFullPath))
        {
            MessageBox.Show(
                this,
                $"The selected mount folder is no longer accessible:\n\n{mountFullPath}",
                "Mount WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(mountFullPath).Any())
            {
                MessageBox.Show(
                    this,
                    "The selected mount folder is not empty. Choose an empty folder for the WIM mount.",
                    "Mount WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Imaging Manager could not inspect the selected mount folder.\n\n{ex.Message}",
                "Mount WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using MountWimConfirmDialog confirm = new(imageFullPath, mountFullPath, imageInfo.Images);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await RunWimMountAsync(imageFullPath, mountFullPath, confirm.SelectedImage);
    }

    private async Task RunWimMountAsync(string imagePath, string mountDirectory, WimImageInfo image)
    {
        if (!TryBeginOperation("Mount WIM"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimMountProgressDialog progressDialog = new(imagePath, mountDirectory, image);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.MountAsync(
                imagePath,
                image.Index,
                mountDirectory,
                progress,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        if (result.Success)
            ClearPendingWimUnmount(mountDirectory);

        await RefreshMountedWimStateAsync(result.Success ? mountDirectory : null);

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Mount WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            $"The WIM image was mounted successfully.\n\nImage: {image.DisplayName}\nMount folder: {mountDirectory}\n\nThe image is mounted read/write. Unmount it with Commit to save changes or Discard to abandon them when finished.",
            "Mount WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task RefreshViewAsync()
    {
        int? selectedDiskNumber = GetSelectedDisk()?.DiskNumber;
        string? selectedMountDirectory = GetSelectedMountedWim()?.MountDirectory;
        await RequestDiskRefreshAsync(
            selectedDiskNumber,
            preferredMountDirectory: selectedMountDirectory);
        await RefreshMountedWimStateAsync(selectedMountDirectory, "Refresh Mounted WIMs");
    }

    private void LoadPendingWimUnmountState()
    {
        _pendingWimUnmounts.Clear();
        foreach (PendingWimUnmountState state in PendingWimUnmountStateStore.Load())
        {
            if (string.IsNullOrWhiteSpace(state.MountDirectory))
                continue;

            try
            {
                _pendingWimUnmounts[NormalizeMountDirectoryKey(state.MountDirectory)] = state;
            }
            catch
            {
                // Ignore a malformed recovery record rather than blocking Imaging Manager startup.
            }
        }
    }

    private static string NormalizeMountDirectoryKey(string mountDirectory)
    {
        string fullPath = Path.GetFullPath(mountDirectory);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private bool IsPendingWimUnmount(WimMountedImageInfo image)
    {
        if (string.IsNullOrWhiteSpace(image.MountDirectory))
            return false;

        string key = NormalizeMountDirectoryKey(image.MountDirectory);
        if (!_pendingWimUnmounts.TryGetValue(key, out PendingWimUnmountState? state))
            return false;

        bool sameImage = string.IsNullOrWhiteSpace(state.ImageFile) ||
                         string.IsNullOrWhiteSpace(image.ImageFile) ||
                         PathsEqual(state.ImageFile, image.ImageFile);
        bool sameIndex = state.ImageIndex <= 0 || image.ImageIndex <= 0 || state.ImageIndex == image.ImageIndex;
        return sameImage && sameIndex;
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private void MarkPendingWimUnmount(WimMountedImageInfo image)
    {
        string key = NormalizeMountDirectoryKey(image.MountDirectory);
        _pendingWimUnmounts[key] = new PendingWimUnmountState
        {
            MountDirectory = image.MountDirectory,
            ImageFile = image.ImageFile,
            ImageIndex = image.ImageIndex
        };
        SavePendingWimUnmountState();
    }

    private void ClearPendingWimUnmount(string mountDirectory)
    {
        if (string.IsNullOrWhiteSpace(mountDirectory))
            return;

        if (_pendingWimUnmounts.Remove(NormalizeMountDirectoryKey(mountDirectory)))
            SavePendingWimUnmountState();
    }

    private void ReconcilePendingWimUnmountState(IReadOnlyList<WimMountedImageInfo> mountedImages)
    {
        HashSet<string> activeKeys = mountedImages
            .Where(static image => !string.IsNullOrWhiteSpace(image.MountDirectory))
            .Select(image => NormalizeMountDirectoryKey(image.MountDirectory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] staleKeys = _pendingWimUnmounts.Keys
            .Where(key => !activeKeys.Contains(key))
            .ToArray();

        if (staleKeys.Length == 0)
            return;

        foreach (string key in staleKeys)
            _pendingWimUnmounts.Remove(key);
        SavePendingWimUnmountState();
    }

    private void SavePendingWimUnmountState() =>
        PendingWimUnmountStateStore.Save(_pendingWimUnmounts.Values);

    private static bool IsPartialUnmountCommitError(WimOperationResult result) =>
        result.Output.Contains("0xc142011d", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectoryStillOpenUnmountError(WimOperationResult result) =>
        result.Output.Contains("0xc1420117", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> RefreshMountedWimStateAsync(
        string? preferredMountDirectory = null,
        string? errorTitle = null)
    {
        if (_operationActive || IsDisposed)
            return false;

        string? selectedMountDirectory = preferredMountDirectory ?? GetSelectedMountedWim()?.MountDirectory;
        WimMountedImageInfoResult result;
        if (errorTitle != null)
            SetWaitCursorState(true);
        try
        {
            result = await _wimBackend.GetMountedImagesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimMountedImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            if (errorTitle != null && !IsDisposed)
                SetWaitCursorState(false);
        }

        if (!result.Success)
        {
            // Preserve the last known rows when inventory temporarily fails.
            if (errorTitle != null && !IsDisposed)
            {
                string details = string.IsNullOrWhiteSpace(result.Output)
                    ? $"DISM exited with code {result.ExitCode}."
                    : result.Output;
                MessageBox.Show(
                    this,
                    "Imaging Manager could not read the mounted WIM inventory.\n\n" + details,
                    errorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            if (!IsDisposed)
                UpdateSelectedDiskPanel();
            return false;
        }

        _mountedWims = result.Images;
        ReconcilePendingWimUnmountState(_mountedWims);
        RebuildMountedWimTiles(selectedMountDirectory, updateUi: false);
        if (!IsDisposed)
            UpdateSelectedDiskPanel();
        return true;
    }

    private async Task<WimMountedImageInfo?> ResolveSelectedMountedWimForActionAsync(string title)
    {
        WimMountedImageInfo? selected = GetSelectedMountedWim();
        if (selected == null)
            return null;

        if (!await RefreshMountedWimStateAsync(selected.MountDirectory, title))
            return null;

        WimMountedImageInfo? current = _mountedWims.FirstOrDefault(image =>
            PathsEqual(image.MountDirectory, selected.MountDirectory));

        if (current == null)
        {
            MessageBox.Show(
                this,
                "The selected WIM is no longer mounted. The mounted-image list has been refreshed.",
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return current;
    }

    private async Task UnmountWimAsync()
    {
        if (_operationActive)
            return;

        WimMountedImageInfo? selected = await ResolveSelectedMountedWimForActionAsync("Unmount WIM");
        if (selected == null)
            return;

        if (IsPendingWimUnmount(selected))
        {
            DialogResult finish = MessageBox.Show(
                this,
                $"The changes for this WIM have already been committed. Only the mount still needs to be released.\n\n{selected.DisplayName}\n\nClose any files, folders, or applications using the mount directory, then choose Yes to finish the unmount without committing again.",
                "Finish Unmount",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (finish == DialogResult.Yes)
                await RunPendingWimUnmountAsync(selected);
            return;
        }

        using UnmountWimDialog dialog = new(new[] { selected });
        DialogResult choice = dialog.ShowDialog(this);
        if (choice != DialogResult.Yes && choice != DialogResult.No)
            return;

        if (choice == DialogResult.Yes)
            await RunWimCommitAndUnmountAsync(selected);
        else
            await RunWimDiscardUnmountAsync(selected);
    }

    private async Task RunWimCommitAndUnmountAsync(WimMountedImageInfo image)
    {
        if (!TryBeginOperation("Unmount WIM"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Unmount WIM",
            "Saving changes to WIM",
            image.MountDirectory);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult commitResult = new()
        {
            Success = false,
            ExitCode = -1,
            Output = "The WIM commit did not start."
        };
        WimOperationResult? unmountResult = null;

        try
        {
            try
            {
                commitResult = await _wimBackend.CommitAsync(
                    image.MountDirectory,
                    progress,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                commitResult = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
            }

            if (commitResult.Success)
            {
                // Persist the committed state before attempting to release the mount. If the
                // unmount is blocked by an open handle (or the app closes unexpectedly), the
                // next UI pass knows not to commit the same image again.
                MarkPendingWimUnmount(image);
                progressDialog.BeginPhase(
                    "Releasing mounted WIM",
                    image.MountDirectory,
                    "The WIM is saved. Finishing the unmount...");

                try
                {
                    unmountResult = await _wimBackend.UnmountDiscardAsync(
                        image.MountDirectory,
                        progress,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    unmountResult = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
                }

                if (unmountResult.Success)
                    ClearPendingWimUnmount(image.MountDirectory);
            }
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync(image.MountDirectory);

        if (!commitResult.Success)
        {
            if (IsPartialUnmountCommitError(commitResult))
            {
                DialogResult recover = MessageBox.Show(
                    this,
                    "DISM reports that this image is in a partial-unmount state and cannot be committed again (0xc142011d). This commonly occurs when a previous unmount-with-commit saved the WIM but could not release the mount directory.\n\nIf that is what happened, the previous commit probably succeeded. Do not commit it again.\n\nTreat this WIM as already committed and attempt an unmount-only recovery now?",
                    "WIM Already Partially Unmounted",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (recover == DialogResult.Yes)
                {
                    MarkPendingWimUnmount(image);
                    RebuildMountedWimTiles(image.MountDirectory, updateUi: false);
                    UpdateSelectedDiskPanel();
                    await RunPendingWimUnmountAsync(image);
                }
                return;
            }

            ShowWimOperationFailure("Commit WIM Failed", commitResult);
            return;
        }

        if (unmountResult is { Success: false })
        {
            ShowCommittedPendingUnmountFailure(image, unmountResult);
            return;
        }

        MessageBox.Show(
            this,
            $"The WIM was committed and unmounted successfully.\n\n{image.ImageFile}",
            "Unmount WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task RunWimDiscardUnmountAsync(WimMountedImageInfo image)
    {
        if (!TryBeginOperation("Discard WIM"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Unmount WIM",
            "Discarding changes and unmounting WIM",
            image.MountDirectory);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.UnmountDiscardAsync(
                image.MountDirectory,
                progress,
                CancellationToken.None);
            if (result.Success)
                ClearPendingWimUnmount(image.MountDirectory);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync(image.MountDirectory);

        if (!result.Success)
        {
            ShowWimOperationFailure("Unmount WIM Failed", result);
            return;
        }

        MessageBox.Show(
            this,
            $"The mounted changes were discarded and the WIM was unmounted successfully.\n\n{image.ImageFile}",
            "Unmount WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task RunPendingWimUnmountAsync(WimMountedImageInfo image)
    {
        if (!TryBeginOperation("Finish Unmount"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Finish Unmount",
            "Finishing WIM unmount",
            image.MountDirectory);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.UnmountDiscardAsync(
                image.MountDirectory,
                progress,
                CancellationToken.None);
            if (result.Success)
                ClearPendingWimUnmount(image.MountDirectory);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync(image.MountDirectory);

        if (!result.Success)
        {
            ShowCommittedPendingUnmountFailure(image, result);
            return;
        }

        MessageBox.Show(
            this,
            $"The already-committed WIM was unmounted successfully.\n\n{image.ImageFile}",
            "Finish Unmount",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowCommittedPendingUnmountFailure(WimMountedImageInfo image, WimOperationResult result)
    {
        string reason = IsDirectoryStillOpenUnmountError(result)
            ? "The mount directory could not be released because a file, folder, or application still has something open inside it."
            : "DISM could not release the mount directory.";
        string details = string.IsNullOrWhiteSpace(result.Output)
            ? $"DISM exited with code {result.ExitCode}."
            : result.Output;

        MessageBox.Show(
            this,
            $"The WIM was committed successfully, but the unmount did not complete.\n\n{reason}\n\nThe changes are already saved to the WIM. Do not commit this mount again. Close anything using the mount directory, then select it and use Finish Unmount.\n\nMount: {image.MountDirectory}\n\n{details}",
            "WIM Committed - Unmount Pending",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void ShowWimOperationFailure(string title, WimOperationResult result)
    {
        string details = string.IsNullOrWhiteSpace(result.Output)
            ? $"DISM exited with code {result.ExitCode}."
            : result.Output;
        MessageBox.Show(this, details, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private async Task RemountWimAsync()
    {
        if (_operationActive)
            return;

        WimMountedImageInfo? selected = await ResolveSelectedMountedWimForActionAsync("Remount WIM");
        if (selected == null)
            return;

        if (!IsMountedWimStatus(selected, "Needs Remount"))
        {
            MessageBox.Show(
                this,
                "The selected WIM no longer requires a remount. The mounted-image list has been refreshed.",
                "Remount WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            $"Remount this inaccessible WIM so it can be serviced again?\n\n{selected.DisplayName}",
            "Remount WIM",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        if (!TryBeginOperation("Remount WIM"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Remount WIM",
            "Remounting WIM",
            selected.MountDirectory);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.RemountAsync(
                selected.MountDirectory,
                progress,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync(selected.MountDirectory);

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Remount WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            $"The WIM was remounted successfully.\n\n{selected.ImageFile}",
            "Remount WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task CleanupMountsAsync()
    {
        if (_operationActive)
            return;

        if (!await RefreshMountedWimStateAsync(errorTitle: "Cleanup Mounts"))
            return;
        WimMountedImageInfo[] invalidMounts = _mountedWims
            .Where(static image => IsMountedWimStatus(image, "Invalid"))
            .ToArray();

        if (invalidMounts.Length == 0)
        {
            MessageBox.Show(
                this,
                "DISM no longer reports any invalid WIM mounts. The mounted-image list has been refreshed.",
                "Cleanup Mounts",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string invalidSummary = invalidMounts.Length == 1
            ? "DISM currently reports 1 invalid WIM mount."
            : $"DISM currently reports {invalidMounts.Length} invalid WIM mounts.";

        DialogResult confirm = MessageBox.Show(
            this,
            $"{invalidSummary}\n\nDISM Cleanup-Mountpoints is a system-wide cleanup operation. It removes resources associated with corrupted mounted images. It does not unmount healthy images and does not remove mounts that can be recovered with Remount WIM.\n\nContinue with cleanup?",
            "Cleanup Mounts",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        if (!TryBeginOperation("Cleanup Mounts"))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Cleanup Mounts",
            "Cleaning corrupted WIM mount resources",
            invalidSummary);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.CleanupMountpointsAsync(
                progress,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync();

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Cleanup Mounts Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            "DISM completed the corrupted mount-point cleanup successfully. The mounted-WIM inventory has been refreshed.",
            "Cleanup Mounts",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task AddDriversAsync()
    {
        if (_operationActive)
            return;

        WimMountedImageInfo? mountedWim = GetSelectedMountedWim();
        ImagingPartitionInfo? partition = GetSelectedPartition();

        string imageRoot;
        string targetLabel;
        bool changesRequireCommit;
        ImagingDiskInfo? targetDisk = null;

        if (mountedWim != null)
        {
            WimMountedImageInfo? current = await ResolveSelectedMountedWimForActionAsync("Add Drivers");
            if (current == null)
                return;

            if (!current.ReadWrite)
            {
                MessageBox.Show(
                    this,
                    "The selected WIM is mounted read-only. Drivers can only be added to a WIM mounted read/write.",
                    "Add Drivers",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            imageRoot = current.MountDirectory;
            targetLabel = $"Mounted WIM:\n{current.DisplayName}";
            changesRequireCommit = true;
        }
        else if (partition != null && TryGetOfflineWindowsRoot(partition, out string offlineWindowsRoot))
        {
            targetDisk = GetSelectedDisk();
            imageRoot = offlineWindowsRoot;
            targetLabel = $"Offline Windows installation:\n{GetPartitionDisplayName(partition)} — {offlineWindowsRoot}";
            changesRequireCommit = false;
        }
        else
        {
            return;
        }

        string? driverFolder = RunExplorerFolderPicker("Select driver folder to add recursively");
        if (string.IsNullOrWhiteSpace(driverFolder))
            return;

        string driverFullPath;
        try
        {
            driverFullPath = Path.GetFullPath(driverFolder);
            if (!Directory.Exists(driverFullPath))
                throw new DirectoryNotFoundException($"The selected driver folder is no longer accessible: {driverFullPath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add Drivers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string commitNote = changesRequireCommit
            ? "\n\nThe changes remain pending until the WIM is unmounted with Commit."
            : "\n\nThe drivers are added directly to the selected offline Windows installation.";

        DialogResult confirm = MessageBox.Show(
            this,
            $"Add all INF driver packages from this folder and its subfolders?\n\n{targetLabel}\n\nDriver folder:\n{driverFullPath}{commitNote}",
            "Add Drivers",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        await RunAddDriversAsync(imageRoot, targetLabel, driverFullPath, changesRequireCommit, targetDisk);
    }

    private async Task RunAddDriversAsync(
        string imageRoot,
        string targetLabel,
        string driverFolder,
        bool changesRequireCommit,
        ImagingDiskInfo? targetDisk)
    {
        if (!TryBeginOperation("Add Drivers", targetDisk))
            return;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Add Drivers",
            "Adding drivers to offline Windows image",
            imageRoot);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.AddDriversAsync(
                imageRoot,
                driverFolder,
                true,
                progress,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            EndOperation();
            Enabled = true;
            Activate();
        }

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Add Drivers Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string successNote = changesRequireCommit
            ? "\n\nUnmount the WIM with Commit to save the changes."
            : string.Empty;
        MessageBox.Show(
            this,
            $"The driver packages were added successfully.\n\n{targetLabel}{successNote}",
            "Add Drivers",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
