using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task CaptureSelectedDiskAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || _operationActive)
            return;

        FfuCaptureAssessment assessment = FfuCaptureAssessment.Evaluate(disk);
        if (assessment.Suitability == FfuCaptureSuitability.BitLockerStatusUnknown)
        {
            string detail = string.IsNullOrWhiteSpace(disk.BitLockerStatusError)
                ? string.Empty
                : $"\n\nStatus error:\n{disk.BitLockerStatusError}";

            DialogResult continueWithoutStatus = MessageBox.Show(
                this,
                "Imaging Manager could not verify the BitLocker encryption state of this disk." +
                "\n\nFFU capture of encrypted disks is unsupported. Verify that all source volumes are fully decrypted before capture." +
                detail +
                "\n\nContinue to the capture dialog anyway?",
                "BitLocker Status Unavailable",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (continueWithoutStatus != DialogResult.Yes)
                return;
        }

        if (assessment.RequiresEncryptionWarning)
        {
            using EncryptedCaptureWarningDialog warning = new(disk, assessment);
            DialogResult result = warning.ShowDialog(this);
            if (result == DialogResult.Retry)
            {
                LaunchBitLockerManager(assessment.AffectedVolumes.FirstOrDefault()?.MountPoint);
                return;
            }

            if (result != DialogResult.Ignore)
                return;
        }

        string? imagePath = RunExplorerPicker(save: true, title: $"Capture Disk {disk.DiskNumber} to FFU");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!imagePath.EndsWith(".ffu", StringComparison.OrdinalIgnoreCase))
            imagePath += ".ffu";

        string? preflightError = ImagingPreflight.ValidateCaptureDestination(disk, _disks, imagePath);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Capture FFU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (File.Exists(imagePath))
        {
            DialogResult replace = MessageBox.Show(
                this,
                $"The file already exists:\n\n{imagePath}\n\nReplace it?",
                "Capture FFU",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes)
                return;
        }

        string defaultName = Path.GetFileNameWithoutExtension(imagePath);
        using CaptureMetadataDialog metadata = new(defaultName);
        if (metadata.ShowDialog(this) != DialogResult.OK)
            return;

        await RunOperationAsync(
            FfuOperationKind.Capture,
            disk,
            imagePath,
            (progress, token) => _ffuBackend.CaptureAsync(disk, imagePath, metadata.ImageName, metadata.Description, progress, token));
    }

    private async Task ApplyToSelectedDiskAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || _operationActive)
            return;

        string? imagePath = RunExplorerPicker(save: false, title: $"Select FFU to apply to Disk {disk.DiskNumber}");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected FFU file no longer exists.", "Apply FFU", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string? preflightError = ImagingPreflight.ValidateApplySourceAndRuntime(disk, _disks, imagePath, AppContext.BaseDirectory);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Apply FFU", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using ApplyFfuConfirmDialog confirm = new(disk, imagePath);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await RunOperationAsync(
            FfuOperationKind.Apply,
            disk,
            imagePath,
            (progress, token) => _ffuBackend.ApplyAsync(disk, imagePath, progress, token));
    }

    private async Task CaptureSelectedPartitionWimAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        if (disk == null || partition == null || _operationActive)
            return;

        string partitionName = GetPartitionDisplayName(partition);
        string? imagePath = RunExplorerPicker(
            save: true,
            title: $"Capture {partitionName} to WIM",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!imagePath.EndsWith(".wim", StringComparison.OrdinalIgnoreCase))
            imagePath += ".wim";

        string? preflightError = ImagingPreflight.ValidateWimCaptureDestination(partition, imagePath);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Capture WIM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool appendToExistingWim = false;
        if (File.Exists(imagePath))
        {
            using ExistingWimCaptureDialog existingWim = new(imagePath);
            DialogResult existingAction = existingWim.ShowDialog(this);
            if (existingAction == DialogResult.Cancel)
                return;

            appendToExistingWim = existingAction == DialogResult.Yes;
        }

        string defaultName = Path.GetFileNameWithoutExtension(imagePath);
        using WimCaptureMetadataDialog metadata = new(defaultName);
        if (metadata.ShowDialog(this) != DialogResult.OK)
            return;

        if (!TryBeginOperation("Capture WIM", disk))
            return;
        UpdateSelectedDiskPanel();

        TemporaryDriveLetterResult? temporarySourceMount = null;
        string sourceRoot = string.Empty;
        bool stagedWinRe = false;
        bool operationRan = false;
        try
        {
            if (!TryGetPartitionCaptureRoot(partition, out sourceRoot))
            {
                SetWaitCursorState(true);
                TemporaryDriveLetterResult mountResult;
                try
                {
                    mountResult = _temporaryDriveLetters.Assign(disk.DiskNumber, partition.PartitionNumber);
                }
                finally
                {
                    SetWaitCursorState(false);
                }

                if (!mountResult.Success)
                {
                    MessageBox.Show(
                        this,
                        "The selected partition does not currently have an accessible drive letter, and Imaging Manager could not temporarily mount it for WIM capture.\n\n" +
                        mountResult.Error,
                        "Capture WIM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                temporarySourceMount = mountResult;
                sourceRoot = mountResult.Root;
            }

            // WinRE staging is a Windows-installation convenience only. Data,
            // EFI, recovery, and other mountable partitions go straight to
            // DISM without being classified or blocked by the UI.
            if (_winReStaging.IsWindowsInstallation(sourceRoot) && !File.Exists(_winReStaging.GetWinRePath(sourceRoot)))
            {
                DialogResult stageChoice = MessageBox.Show(
                    this,
                    $"A Windows installation was detected on {sourceRoot.TrimEnd('\\')}, but winre.wim is not present at:\n\n" +
                    $"{_winReStaging.GetWinRePath(sourceRoot)}\n\n" +
                    "Try to retrieve the configured winre.wim from this Windows installation's Recovery partition before capture?\n\n" +
                    "Yes = temporarily mount the configured Recovery partition, copy winre.wim into the Windows tree, then remove the temporary drive letter.\n" +
                    "No = capture the partition without staging winre.wim.\n" +
                    "Cancel = stop the capture.",
                    "Windows RE Not in Windows Partition",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                if (stageChoice == DialogResult.Cancel)
                    return;

                if (stageChoice == DialogResult.Yes)
                {
                    SetWaitCursorState(true);
                    WinReStageResult stageResult;
                    try
                    {
                        stageResult = _winReStaging.StageFromConfiguredRecoveryPartition(sourceRoot);
                    }
                    finally
                    {
                        SetWaitCursorState(false);
                    }

                    if (!stageResult.Success)
                    {
                        DialogResult continueWithoutWinRe = MessageBox.Show(
                            this,
                            "Imaging Manager could not stage winre.wim from the configured Recovery partition.\n\n" +
                            stageResult.Error +
                            "\n\nCapture the WIM without winre.wim?",
                            "Windows RE Staging Failed",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2);

                        if (continueWithoutWinRe != DialogResult.Yes)
                            return;
                    }
                    else
                    {
                        stagedWinRe = stageResult.StagedByImagingManager;

                        if (!string.IsNullOrWhiteSpace(stageResult.Warning))
                        {
                            DialogResult continueWithWarning = MessageBox.Show(
                                this,
                                "winre.wim was staged successfully, but cleanup of the temporary Recovery drive letter reported a problem:\n\n" +
                                stageResult.Warning +
                                "\n\nContinue with the WIM capture?",
                                "Windows RE Staging Warning",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2);

                            if (continueWithWarning != DialogResult.Yes)
                            {
                                TryRemoveStagedWinRe(sourceRoot);
                                return;
                            }
                        }
                    }
                }
            }

            if (!appendToExistingWim && File.Exists(imagePath))
            {
                try
                {
                    File.Delete(imagePath);
                }
                catch (Exception ex)
                {
                    if (stagedWinRe)
                        TryRemoveStagedWinRe(sourceRoot);

                    MessageBox.Show(
                        this,
                        $"The existing WIM could not be replaced:\n\n{imagePath}\n\n{ex.Message}",
                        "Capture WIM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            operationRan = true;
            await RunWimCaptureAsync(
                partition,
                sourceRoot,
                imagePath,
                metadata.ImageName,
                metadata.Description,
                stagedWinRe,
                appendToExistingWim);
        }
        finally
        {
            if (temporarySourceMount != null)
            {
                string? removalError = _temporaryDriveLetters.Remove(temporarySourceMount);
                if (!string.IsNullOrWhiteSpace(removalError))
                {
                    MessageBox.Show(
                        this,
                        "Imaging Manager could not remove the temporary drive letter assigned to the captured partition.\n\n" +
                        removalError,
                        "Partition Cleanup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            EndOperation();

            if (temporarySourceMount != null || operationRan)
                await RequestDiskRefreshAsync(disk.DiskNumber);
        }
    }

    private async Task DeployWimToSelectedDiskAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || GetSelectedPartition() != null || _operationActive)
            return;

        string? imagePath = RunExplorerPicker(
            save: false,
            title: $"Select WIM to deploy to Disk {disk.DiskNumber}",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Deploy WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string? preflightError = ImagingPreflight.ValidateWimDeploySourceAndRuntime(
            disk,
            _disks,
            imagePath,
            AppContext.BaseDirectory);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Deploy WIM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WimImageInfoResult? imageInfo = await TryLoadWimImageInfoAsync(imagePath, "Deploy WIM");
        if (imageInfo == null)
            return;

        WimDeploymentFirmwareType firmwareType = _wimDeployment.DetectFirmwareType();
        if (firmwareType == WimDeploymentFirmwareType.Unknown)
        {
            MessageBox.Show(
                this,
                "Imaging Manager could not determine whether WinPE was booted in UEFI or BIOS firmware mode.\n\n" +
                "Deploy WIM uses the current WinPE firmware mode to choose the GPT or MBR disk layout, so deployment was not started.",
                "Deploy WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using DeployWimConfirmDialog confirm = new(disk, imagePath, imageInfo.Images, firmwareType);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await RunWimDeployAsync(
            disk,
            imagePath,
            confirm.SelectedImage,
            firmwareType,
            confirm.AssignTargetToC);
    }

    private async Task<WimImageInfoResult?> TryLoadWimImageInfoAsync(string imagePath, string title)
    {
        WimImageInfoResult result;
        SetWaitCursorState(true);
        try
        {
            result = await _wimBackend.GetImagesAsync(imagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            SetWaitCursorState(false);
        }

        if (result.Success && result.Images.Count > 0)
            return result;

        string details = string.IsNullOrWhiteSpace(result.Output)
            ? $"DISM exited with code {result.ExitCode}."
            : result.Output;
        MessageBox.Show(
            this,
            "Imaging Manager could not read the image list from the selected WIM.\n\n" + details,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return null;
    }

    private async Task RunWimDeployAsync(
        ImagingDiskInfo disk,
        string imagePath,
        WimImageInfo image,
        WimDeploymentFirmwareType firmwareType,
        bool assignTargetToC)
    {
        if (!TryBeginOperation("Deploy WIM", disk))
            return;

        TemporaryDriveLetterReservation? windowsLetterReservation = null;
        string effectiveImagePath = imagePath;
        char windowsDriveLetter = 'C';

        try
        {
            if (assignTargetToC)
            {
                // If C: already belongs to the target disk, DiskPart clean will release it.
                // Otherwise move the current C: owner only because the user explicitly
                // requested C: for this deployment.
                if (!disk.ContainsDrive(@"C:\"))
                {
                    SetWaitCursorState(true);
                    DriveLetterReassignmentResult cResult;
                    try
                    {
                        cResult = _driveLetterReassignment.MoveCToLowestAvailable(
                            AppContext.BaseDirectory,
                            'S',
                            'R');
                    }
                    finally
                    {
                        SetWaitCursorState(false);
                    }

                    if (!cResult.Success)
                    {
                        MessageBox.Show(
                            this,
                            "Imaging Manager could not make C: available for the deployment.\n\n" + cResult.Error,
                            "Deploy WIM - Drive Letter",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    if (cResult.Changed)
                    {
                        effectiveImagePath = DriveLetterReassignmentService.RebasePathFromDisplacedC(
                            effectiveImagePath,
                            cResult.DisplacedCRoot);
                    }
                }
            }
            else
            {
                try
                {
                    windowsLetterReservation = _temporaryDriveLetters.ReserveAvailable('C', 'S', 'R', 'X');
                    windowsDriveLetter = windowsLetterReservation.DriveLetter;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Imaging Manager could not reserve a temporary drive letter for the deployed Windows partition.\n\n" + ex.Message,
                        "Deploy WIM - Drive Letter",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            if (!File.Exists(effectiveImagePath))
            {
                MessageBox.Show(
                    this,
                    $"The selected WIM file is no longer accessible after preparing the deployment drive letters.\n\n{effectiveImagePath}",
                    "Deploy WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            UpdateSelectedDiskPanel();
            Enabled = false;

            using WimDeployProgressDialog progressDialog = new(disk, effectiveImagePath, image);
            using CancellationTokenSource cts = new();
            progressDialog.CancelRequested += (_, _) => cts.Cancel();
            progressDialog.Show(this);

            Progress<WimDeploymentProgress> progress = new(update => progressDialog.UpdateProgress(update));
            WimDeploymentResult result;
            try
            {
                result = await _wimDeployment.DeployAsync(
                    disk,
                    effectiveImagePath,
                    image,
                    firmwareType,
                    windowsDriveLetter,
                    progress,
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = new WimDeploymentResult
                {
                    Success = false,
                    Canceled = true,
                    FirmwareType = firmwareType
                };
            }
            catch (Exception ex)
            {
                result = new WimDeploymentResult
                {
                    Success = false,
                    Canceled = false,
                    FirmwareType = firmwareType,
                    Output = ex.Message
                };
            }
            finally
            {
                progressDialog.AllowClose();
                progressDialog.Close();
                Enabled = true;
                Activate();
            }

            if (result.Canceled)
            {
                MessageBox.Show(
                    this,
                    "The WIM deployment was canceled. The target disk may have already been erased or may contain a partially deployed image. Do not boot it until deployment completes successfully.",
                    "Deploy WIM Canceled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else if (!result.Success)
            {
                string details = string.IsNullOrWhiteSpace(result.Output)
                    ? "The deployment did not complete successfully."
                    : result.Output;

                if (result.Warnings.Count > 0)
                    details += "\n\nWarnings:\n" + string.Join("\n", result.Warnings.Select(static warning => "- " + warning));

                MessageBox.Show(this, details, "Deploy WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (result.Warnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    $"The WIM was deployed to Disk {disk.DiskNumber}, but deployment completed with warnings:\n\n" +
                    string.Join("\n", result.Warnings.Select(static warning => "- " + warning)),
                    "Deploy WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(
                    this,
                    $"The WIM was deployed successfully to Disk {disk.DiskNumber}.\n\nImage: {image.DisplayName}",
                    "Deploy WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        finally
        {
            if (windowsLetterReservation != null)
                _temporaryDriveLetters.Release(windowsLetterReservation);

            EndOperation();

            if (!IsDisposed && !Disposing)
                await RequestDiskRefreshAsync(disk.DiskNumber);
        }
    }

    private async Task MountWimAsync()
    {
        if (_operationActive)
            return;

        string? imagePath = RunExplorerPicker(
            save: false,
            title: "Select WIM to mount",
            extension: ".wim");
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
            UpdateSelectedDiskPanel();
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
        RebuildMountedWimTiles(selectedMountDirectory);
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
        RebuildMountedWimTiles(current?.MountDirectory);
        UpdateSelectedDiskPanel();

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
                    RebuildMountedWimTiles(image.MountDirectory);
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
            UpdateSelectedDiskPanel();
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

    private async Task ExportWimAsync()
    {
        if (_operationActive)
            return;

        string? sourcePath = RunExplorerPicker(
            save: false,
            title: "Select WIM to export",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (!File.Exists(sourcePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Export WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        WimImageInfoResult? imageInfo = await TryLoadWimImageInfoAsync(sourcePath, "Export WIM");
        if (imageInfo == null)
            return;

        string? destinationPath = RunExplorerPicker(
            save: true,
            title: "Export image to WIM",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return;

        if (!destinationPath.EndsWith(".wim", StringComparison.OrdinalIgnoreCase))
            destinationPath += ".wim";

        string sourceFullPath;
        string destinationFullPath;
        try
        {
            sourceFullPath = Path.GetFullPath(sourcePath);
            destinationFullPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "The export destination must be a different WIM file from the source.",
                "Export WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (File.Exists(destinationFullPath))
        {
            DialogResult replace = MessageBox.Show(
                this,
                $"The file already exists:\n\n{destinationFullPath}\n\nReplace it?",
                "Export WIM",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes)
                return;
        }

        using ExportWimConfirmDialog confirm = new(sourceFullPath, destinationFullPath, imageInfo.Images);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await RunWimExportAsync(sourceFullPath, destinationFullPath, confirm.SelectedImage);
    }

    private async Task RunWimExportAsync(string sourcePath, string destinationPath, WimImageInfo image)
    {
        if (!TryBeginOperation("Export WIM"))
            return;

        if (File.Exists(destinationPath))
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch (Exception ex)
            {
                EndOperation();
                MessageBox.Show(
                    this,
                    $"The existing destination WIM could not be replaced:\n\n{destinationPath}\n\n{ex.Message}",
                    "Export WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimExportProgressDialog progressDialog = new(sourcePath, destinationPath, image);
        using CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.ExportAsync(
                sourcePath,
                image.Index,
                destinationPath,
                progress,
                cts.Token);
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
            UpdateSelectedDiskPanel();
            Activate();
        }

        if (!result.Success)
            TryDeletePartialCaptureOutput(destinationPath);

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                "The WIM export operation was canceled. The partial destination WIM was removed.",
                "Export WIM Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Export WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(
                this,
                $"The WIM image was exported successfully.\n\nImage: {image.DisplayName}\nDestination: {destinationPath}",
                "Export WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task ApplyWimToSelectedPartitionAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        if (disk == null || partition == null || _operationActive)
            return;

        string partitionName = GetPartitionDisplayName(partition);
        string? imagePath = RunExplorerPicker(
            save: false,
            title: $"Select WIM to apply to {partitionName}",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Apply WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string? preflightError = ImagingPreflight.ValidateWimApplySourceAndRuntime(partition, imagePath, AppContext.BaseDirectory);
        if (preflightError != null)
        {
            MessageBox.Show(this, preflightError, "Apply WIM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        WimImageInfoResult? imageInfo = await TryLoadWimImageInfoAsync(imagePath, "Apply WIM");
        if (imageInfo == null)
            return;

        if (!TryBeginOperation("Apply WIM", disk))
            return;
        UpdateSelectedDiskPanel();

        TemporaryDriveLetterResult? temporaryTargetMount = null;
        string targetRoot;
        bool operationRan = false;
        try
        {
            if (!TryGetPartitionCaptureRoot(partition, out targetRoot))
            {
                SetWaitCursorState(true);
                TemporaryDriveLetterResult mountResult;
                try
                {
                    mountResult = _temporaryDriveLetters.Assign(disk.DiskNumber, partition.PartitionNumber);
                }
                finally
                {
                    SetWaitCursorState(false);
                }

                if (!mountResult.Success)
                {
                    MessageBox.Show(
                        this,
                        "The selected partition does not currently have an accessible drive letter, and Imaging Manager could not temporarily mount it as a WIM apply target.\n\n" +
                        mountResult.Error,
                        "Apply WIM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                temporaryTargetMount = mountResult;
                targetRoot = mountResult.Root;
            }

            PartitionFileSystemResult fileSystemResult = _partitionFormatter.GetCurrentFileSystem(targetRoot);
            if (!fileSystemResult.Success)
            {
                MessageBox.Show(
                    this,
                    "Imaging Manager could not safely prepare the selected partition for a clean WIM restore.\n\n" +
                    fileSystemResult.Error,
                    "Apply WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool configureBootByDefault = LooksLikeExistingWindowsInstallation(targetRoot);

            using ApplyWimConfirmDialog confirm = new(
                disk,
                partition,
                targetRoot,
                fileSystemResult.FileSystem,
                imagePath,
                imageInfo.Images,
                configureBootByDefault);
            if (confirm.ShowDialog(this) != DialogResult.OK)
                return;

            WimImageInfo selectedImage = confirm.SelectedImage;
            bool configureBootFiles = confirm.ConfigureBootFiles;
            string effectiveImagePath = imagePath;

            if (confirm.AssignTargetToC &&
                !partition.DriveLetters.Any(static drive =>
                    string.Equals(
                        ImagingPath.NormalizeDriveRoot(drive),
                        @"C:\",
                        StringComparison.OrdinalIgnoreCase)))
            {
                SetWaitCursorState(true);
                try
                {
                    if (temporaryTargetMount != null)
                    {
                        DriveLetterReassignmentResult displacedC = _driveLetterReassignment.MoveCToLowestAvailable(
                            AppContext.BaseDirectory,
                            temporaryTargetMount.DriveLetter);
                        if (!displacedC.Success)
                        {
                            MessageBox.Show(
                                this,
                                "Imaging Manager could not make C: available for the selected target.\n\n" + displacedC.Error,
                                "Apply WIM - Drive Letter",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            return;
                        }

                        effectiveImagePath = DriveLetterReassignmentService.RebasePathFromDisplacedC(
                            effectiveImagePath,
                            displacedC.DisplacedCRoot);
                    }

                    DriveLetterReassignmentResult cResult = _driveLetterReassignment.ReassignPartitionToC(
                        disk.DiskNumber,
                        partition.PartitionNumber,
                        targetRoot,
                        AppContext.BaseDirectory);

                    if (!cResult.Success)
                    {
                        MessageBox.Show(
                            this,
                            "Imaging Manager could not reassign the selected target partition to C:.\n\n" + cResult.Error,
                            "Apply WIM - Drive Letter",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    targetRoot = cResult.TargetRoot;
                    effectiveImagePath = DriveLetterReassignmentService.RebasePathFromDisplacedC(
                        effectiveImagePath,
                        cResult.DisplacedCRoot);

                    if (temporaryTargetMount != null)
                    {
                        // The target no longer owns its temporary letter after becoming C:.
                        _temporaryDriveLetters.ReleaseReservation(temporaryTargetMount);
                        temporaryTargetMount = null;
                    }
                }
                finally
                {
                    SetWaitCursorState(false);
                }

                if (!File.Exists(effectiveImagePath))
                {
                    MessageBox.Show(
                        this,
                        $"The selected WIM file is no longer accessible after reassigning the target to C:.\n\n{effectiveImagePath}",
                        "Apply WIM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }
            else if (confirm.AssignTargetToC)
            {
                targetRoot = @"C:\";
            }

            operationRan = true;
            await RunWimApplyAsync(
                partition,
                targetRoot,
                fileSystemResult.FileSystem,
                effectiveImagePath,
                selectedImage,
                configureBootFiles);
        }
        finally
        {
            if (temporaryTargetMount != null)
            {
                string? removalError = _temporaryDriveLetters.Remove(temporaryTargetMount);
                if (!string.IsNullOrWhiteSpace(removalError))
                {
                    MessageBox.Show(
                        this,
                        "Imaging Manager could not remove the temporary drive letter assigned to the WIM target partition.\n\n" +
                        removalError,
                        "Partition Cleanup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            EndOperation();

            if (temporaryTargetMount != null || operationRan)
                await RequestDiskRefreshAsync(disk.DiskNumber);
        }
    }

    private static bool LooksLikeExistingWindowsInstallation(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
            return false;

        try
        {
            string systemHive = Path.Combine(targetRoot, "Windows", "System32", "Config", "SYSTEM");
            return File.Exists(systemHive);
        }
        catch
        {
            return false;
        }
    }

    private async Task RunWimApplyAsync(
        ImagingPartitionInfo partition,
        string targetRoot,
        string fileSystem,
        string imagePath,
        WimImageInfo image,
        bool configureBootFiles)
    {
        Enabled = false;
        SetWaitCursorState(true);

        PartitionFormatResult formatResult;
        try
        {
            formatResult = await _partitionFormatter.FormatQuickAsync(
                targetRoot,
                fileSystem,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            formatResult = PartitionFormatResult.Failed(ex.Message);
        }
        finally
        {
            SetWaitCursorState(false);
        }

        if (!formatResult.Success)
        {
            Enabled = true;
            Activate();
            MessageBox.Show(
                this,
                "Imaging Manager could not format the selected partition before applying the WIM.\n\n" +
                formatResult.Error,
                "Apply WIM - Format Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using WimApplyProgressDialog progressDialog = new(partition, targetRoot, imagePath, image);
        CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        WimBootConfigurationResult? bootResult = null;
        bool windowsImage = false;
        try
        {
            result = await _wimBackend.ApplyAsync(targetRoot, imagePath, image.Index, progress, cts.Token);

            if (result.Success && !result.Canceled && configureBootFiles)
            {
                string windowsDirectory = Path.Combine(targetRoot, "Windows");
                windowsImage = Directory.Exists(windowsDirectory);
                if (windowsImage)
                {
                    progressDialog.BeginBootConfiguration();
                    bootResult = await _wimDeployment.ConfigureAppliedWindowsBootAsync(
                        windowsDirectory,
                        CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            cts.Dispose();
            Enabled = true;
            Activate();
        }

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                "The WIM apply operation was canceled. The target partition may contain a partially applied image.",
                "Apply WIM Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Apply WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (configureBootFiles && windowsImage && bootResult is { Success: false })
        {
            string details = string.IsNullOrWhiteSpace(bootResult.Output)
                ? $"BCDBoot exited with code {bootResult.ExitCode}."
                : bootResult.Output;
            MessageBox.Show(
                this,
                $"The WIM image was applied successfully to {targetRoot.TrimEnd('\\')}, but Windows boot files could not be configured.\n\n" +
                "The applied Windows installation may not be bootable until BCDBoot is run successfully.\n\n" +
                details,
                "Apply WIM - Boot Configuration Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        else
        {
            string bootText = configureBootFiles
                ? windowsImage
                    ? "\n\nWindows boot files were configured successfully."
                    : "\n\nBoot configuration was requested, but no Windows directory was found in the applied image, so BCDBoot was not run."
                : string.Empty;
            MessageBox.Show(
                this,
                $"The WIM image was applied successfully to {targetRoot.TrimEnd('\\')}.\n\n" +
                $"Image: {image.DisplayName}" + bootText,
                "Apply WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private async Task RunWimCaptureAsync(
        ImagingPartitionInfo partition,
        string sourceRoot,
        string imagePath,
        string imageName,
        string description,
        bool stagedWinRe,
        bool appendToExistingWim)
    {
        Enabled = false;

        using WimCaptureProgressDialog progressDialog = new(partition, sourceRoot, imagePath);
        CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        string cleanupError = string.Empty;
        try
        {
            result = appendToExistingWim
                ? await _wimBackend.AppendAsync(sourceRoot, imagePath, imageName, description, progress, cts.Token)
                : await _wimBackend.CaptureAsync(sourceRoot, imagePath, imageName, description, progress, cts.Token);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            if (stagedWinRe)
            {
                try
                {
                    _winReStaging.RemoveStagedWinRe(sourceRoot);
                }
                catch (Exception ex)
                {
                    cleanupError = ex.Message;
                }
            }

            progressDialog.AllowClose();
            progressDialog.Close();
            cts.Dispose();
            Enabled = true;
            Activate();
        }

        if (!result.Success && !appendToExistingWim)
            TryDeletePartialCaptureOutput(imagePath);

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                "The WIM capture operation was canceled.",
                "Capture WIM Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Capture WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(
                this,
                appendToExistingWim
                    ? $"The image was appended to the WIM successfully.\n\n{imagePath}"
                    : $"The WIM was captured successfully.\n\n{imagePath}",
                "Capture WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        if (!string.IsNullOrWhiteSpace(cleanupError))
        {
            MessageBox.Show(
                this,
                "The capture finished, but Imaging Manager could not remove the temporarily staged winre.wim from the Windows partition.\n\n" +
                cleanupError,
                "Windows RE Cleanup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

    }

    private void TryRemoveStagedWinRe(string sourceRoot)
    {
        try
        {
            _winReStaging.RemoveStagedWinRe(sourceRoot);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The temporarily staged winre.wim could not be removed:\n\n{_winReStaging.GetWinRePath(sourceRoot)}\n\n{ex.Message}",
                "Windows RE Cleanup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task RunOperationAsync(
        FfuOperationKind kind,
        ImagingDiskInfo disk,
        string imagePath,
        Func<IProgress<FfuOperationProgress>, CancellationToken, Task<FfuOperationResult>> operation)
    {
        string operationName = kind == FfuOperationKind.Apply ? "Apply FFU" : "Capture FFU";
        if (!TryBeginOperation(operationName, disk))
            return;

        if (kind == FfuOperationKind.Capture && File.Exists(imagePath))
        {
            try
            {
                File.Delete(imagePath);
            }
            catch (Exception ex)
            {
                EndOperation();
                MessageBox.Show(
                    this,
                    $"The existing FFU could not be replaced:\n\n{imagePath}\n\n{ex.Message}",
                    "Capture FFU",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        UpdateSelectedDiskPanel();
        Enabled = false;

        using OperationProgressDialog progressDialog = new(kind, disk, imagePath);
        CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<FfuOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        FfuOperationResult result;
        try
        {
            result = await operation(progress, cts.Token);
        }
        catch (Exception ex)
        {
            result = new FfuOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            cts.Dispose();
            EndOperation();
            Enabled = true;
            Activate();
        }

        if (kind == FfuOperationKind.Capture && !result.Success)
            TryDeletePartialCaptureOutput(imagePath);

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                kind == FfuOperationKind.Apply
                    ? "The FFU apply operation was canceled. The target disk may be incomplete and should not be booted until a successful image is applied."
                    : "The FFU capture operation was canceled.",
                kind == FfuOperationKind.Apply ? "Apply Canceled" : "Capture Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output) ? $"DISM exited with code {result.ExitCode}." : result.Output;
            MessageBox.Show(this, details, kind == FfuOperationKind.Apply ? "Apply FFU Failed" : "Capture FFU Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(
                this,
                kind == FfuOperationKind.Apply ? "The FFU was applied successfully." : $"The FFU was captured successfully.\n\n{imagePath}",
                kind == FfuOperationKind.Apply ? "Apply FFU" : "Capture FFU",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        await RequestDiskRefreshAsync(disk.DiskNumber);
    }


    private static void TryDeletePartialCaptureOutput(string imagePath)
    {
        try
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
        catch
        {
        }
    }

    private string? RunExplorerPicker(bool save, string title) =>
        RunExplorerPicker(save, title, ".ffu");

    private string? RunExplorerPicker(bool save, string title, string extension) =>
        RunExplorerPickerCore(save ? "--savefile" : "--openfile", title, extension);

    private string? RunExplorerFolderPicker(string title) =>
        RunExplorerPickerCore("--selectfolder", title, extension: null);

    private string? RunExplorerPickerCore(string mode, string title, string? extension)
    {
        string pickerPath = Path.Combine(AppContext.BaseDirectory, "ExplorerPicker.exe");
        if (!File.Exists(pickerPath))
        {
            MessageBox.Show(this, $"ExplorerPicker.exe was not found:\n{pickerPath}", "Imaging Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = pickerPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(mode);
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(title);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                startInfo.ArgumentList.Add("--filter");
                startInfo.ArgumentList.Add(extension);
            }
            startInfo.ArgumentList.Add("--owner-hwnd");
            startInfo.ArgumentList.Add(Handle.ToInt64().ToString(CultureInfo.InvariantCulture));

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start ExplorerPicker.exe.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                if (process.ExitCode != 1 && !string.IsNullOrWhiteSpace(error))
                    MessageBox.Show(this, error.Trim(), "Imaging Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string selected = output.Trim();
            return selected.Length == 0 ? null : selected;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Imaging Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private void LaunchBitLockerManager(string? mountPoint)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "BitLocker.Manager.exe");
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"BitLocker.Manager.exe was not found:\n{path}", "BitLocker Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = path,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };

            if (ShellTheme.DarkMode)
                startInfo.ArgumentList.Add("--dark");

            if (!string.IsNullOrWhiteSpace(mountPoint))
            {
                startInfo.ArgumentList.Add("--drive");
                startInfo.ArgumentList.Add(mountPoint);
            }

            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "BitLocker Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
