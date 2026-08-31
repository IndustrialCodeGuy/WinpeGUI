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

        if (File.Exists(imagePath))
        {
            try
            {
                File.Delete(imagePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"The existing FFU could not be replaced:\n\n{imagePath}\n\n{ex.Message}",
                    "Capture FFU",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

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

        TemporaryDriveLetterResult? temporarySourceMount = null;
        string sourceRoot;

        if (!TryGetPartitionCaptureRoot(partition, out sourceRoot))
        {
            UseWaitCursor = true;
            TemporaryDriveLetterResult mountResult;
            try
            {
                mountResult = _temporaryDriveLetters.Assign(disk.DiskNumber, partition.PartitionNumber);
            }
            finally
            {
                UseWaitCursor = false;
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

        bool stagedWinRe = false;
        bool operationRan = false;
        try
        {
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
                    UseWaitCursor = true;
                    WinReStageResult stageResult;
                    try
                    {
                        stageResult = _winReStaging.StageFromConfiguredRecoveryPartition(sourceRoot);
                    }
                    finally
                    {
                        UseWaitCursor = false;
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

            if (temporarySourceMount != null || operationRan)
                LoadDisks(disk.DiskNumber);
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

        WimImageInfoResult imageInfo;
        UseWaitCursor = true;
        try
        {
            imageInfo = await _wimBackend.GetImagesAsync(imagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            imageInfo = new WimImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (!imageInfo.Success || imageInfo.Images.Count == 0)
        {
            string details = string.IsNullOrWhiteSpace(imageInfo.Output)
                ? $"DISM exited with code {imageInfo.ExitCode}."
                : imageInfo.Output;
            MessageBox.Show(
                this,
                "Imaging Manager could not read the image list from the selected WIM.\n\n" + details,
                "Deploy WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

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

        await RunWimDeployAsync(disk, imagePath, confirm.SelectedImage, firmwareType);
    }

    private async Task RunWimDeployAsync(
        ImagingDiskInfo disk,
        string imagePath,
        WimImageInfo image,
        WimDeploymentFirmwareType firmwareType)
    {
        _operationActive = true;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimDeployProgressDialog progressDialog = new(disk, imagePath, image);
        using CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<WimDeploymentProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimDeploymentResult result;
        try
        {
            result = await _wimDeployment.DeployAsync(
                disk,
                imagePath,
                image,
                firmwareType,
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
            _operationActive = false;
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

        LoadDisks(disk.DiskNumber);
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

        WimImageInfoResult imageInfo;
        UseWaitCursor = true;
        try
        {
            imageInfo = await _wimBackend.GetImagesAsync(imagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            imageInfo = new WimImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (!imageInfo.Success || imageInfo.Images.Count == 0)
        {
            string details = string.IsNullOrWhiteSpace(imageInfo.Output)
                ? $"DISM exited with code {imageInfo.ExitCode}."
                : imageInfo.Output;
            MessageBox.Show(
                this,
                "Imaging Manager could not read the image list from the selected WIM.\n\n" + details,
                "Mount WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

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
        _operationActive = true;
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
            _operationActive = false;
            Enabled = true;
            UpdateSelectedDiskPanel();
            Activate();
        }

        await RefreshMountedWimStateAsync();

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
        LoadDisks(selectedDiskNumber);
        await RefreshMountedWimStateAsync();
    }

    private async Task RefreshMountedWimStateAsync()
    {
        if (_operationActive || IsDisposed)
            return;

        try
        {
            WimMountedImageInfoResult result = await _wimBackend.GetMountedImagesAsync(CancellationToken.None);
            if (result.Success)
                _mountedWims = result.Images;
        }
        catch
        {
            // Preserve the last known mount state if DISM inventory temporarily
            // fails; an action will always re-query before servicing an image.
        }

        if (!IsDisposed)
            UpdateSelectedDiskPanel();
    }

    private async Task<IReadOnlyList<WimMountedImageInfo>?> GetMountedWimsForActionAsync(string title)
    {
        WimMountedImageInfoResult result;
        UseWaitCursor = true;
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
            UseWaitCursor = false;
        }

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(
                this,
                "Imaging Manager could not read the mounted WIM inventory.\n\n" + details,
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }

        _mountedWims = result.Images;
        UpdateSelectedDiskPanel();

        if (_mountedWims.Count == 0)
        {
            MessageBox.Show(
                this,
                "There are no mounted WIM images.",
                title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }

        return _mountedWims;
    }

    private async Task UnmountWimAsync()
    {
        if (_operationActive)
            return;

        IReadOnlyList<WimMountedImageInfo>? mountedWims = await GetMountedWimsForActionAsync("Unmount WIM");
        if (mountedWims == null)
            return;

        using UnmountWimDialog dialog = new(mountedWims);
        DialogResult choice = dialog.ShowDialog(this);
        if (choice != DialogResult.Yes && choice != DialogResult.No)
            return;

        WimMountedImageInfo selected = dialog.SelectedImage;
        bool commitChanges = choice == DialogResult.Yes;
        await RunWimUnmountAsync(selected, commitChanges);
    }

    private async Task RunWimUnmountAsync(WimMountedImageInfo image, bool commitChanges)
    {
        _operationActive = true;
        UpdateSelectedDiskPanel();
        Enabled = false;

        string action = commitChanges ? "Committing and unmounting WIM" : "Discarding changes and unmounting WIM";
        using WimServicingProgressDialog progressDialog = new(
            "Unmount WIM",
            action,
            image.MountDirectory);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.UnmountAsync(
                image.MountDirectory,
                commitChanges,
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
            _operationActive = false;
            Enabled = true;
            Activate();
        }

        await RefreshMountedWimStateAsync();

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Unmount WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(
            this,
            commitChanges
                ? $"The WIM was committed and unmounted successfully.\n\n{image.ImageFile}"
                : $"The mounted changes were discarded and the WIM was unmounted successfully.\n\n{image.ImageFile}",
            "Unmount WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task AddDriversAsync()
    {
        if (_operationActive)
            return;

        IReadOnlyList<WimMountedImageInfo>? mountedWims = await GetMountedWimsForActionAsync("Add Drivers");
        if (mountedWims == null)
            return;

        IReadOnlyList<WimMountedImageInfo> writableWims = mountedWims.Where(static image => image.ReadWrite).ToArray();
        if (writableWims.Count == 0)
        {
            MessageBox.Show(
                this,
                "The mounted WIM image is read-only. Drivers can only be added to a WIM mounted read/write.",
                "Add Drivers",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        WimMountedImageInfo target;
        if (writableWims.Count == 1)
        {
            target = writableWims[0];
        }
        else
        {
            using MountedWimSelectionDialog select = new("Add Drivers", "Select the mounted WIM to service", writableWims);
            if (select.ShowDialog(this) != DialogResult.OK)
                return;
            target = select.SelectedImage;
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

        DialogResult confirm = MessageBox.Show(
            this,
            $"Add all INF driver packages from this folder and its subfolders?\n\nMounted WIM:\n{target.MountDirectory}\n\nDriver folder:\n{driverFullPath}\n\nThe changes remain pending until the WIM is unmounted with Commit.",
            "Add Drivers",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
            return;

        await RunAddDriversAsync(target, driverFullPath);
    }

    private async Task RunAddDriversAsync(WimMountedImageInfo image, string driverFolder)
    {
        _operationActive = true;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Add Drivers",
            "Adding drivers to mounted WIM",
            driverFolder);
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.AddDriversAsync(
                image.MountDirectory,
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
            _operationActive = false;
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

        MessageBox.Show(
            this,
            $"The driver packages were added successfully.\n\nMounted WIM: {image.MountDirectory}\n\nUnmount the WIM with Commit to save the changes.",
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

        WimImageInfoResult imageInfo;
        UseWaitCursor = true;
        try
        {
            imageInfo = await _wimBackend.GetImagesAsync(sourcePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            imageInfo = new WimImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (!imageInfo.Success || imageInfo.Images.Count == 0)
        {
            string details = string.IsNullOrWhiteSpace(imageInfo.Output)
                ? $"DISM exited with code {imageInfo.ExitCode}."
                : imageInfo.Output;
            MessageBox.Show(
                this,
                "Imaging Manager could not read the image list from the selected WIM.\n\n" + details,
                "Export WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

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

        if (File.Exists(destinationFullPath))
        {
            try
            {
                File.Delete(destinationFullPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"The existing destination WIM could not be replaced:\n\n{destinationFullPath}\n\n{ex.Message}",
                    "Export WIM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        await RunWimExportAsync(sourceFullPath, destinationFullPath, confirm.SelectedImage);
    }

    private async Task RunWimExportAsync(string sourcePath, string destinationPath, WimImageInfo image)
    {
        _operationActive = true;
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
            _operationActive = false;
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

        WimImageInfoResult imageInfo;
        UseWaitCursor = true;
        try
        {
            imageInfo = await _wimBackend.GetImagesAsync(imagePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            imageInfo = new WimImageInfoResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            UseWaitCursor = false;
        }

        if (!imageInfo.Success || imageInfo.Images.Count == 0)
        {
            string details = string.IsNullOrWhiteSpace(imageInfo.Output)
                ? $"DISM exited with code {imageInfo.ExitCode}."
                : imageInfo.Output;
            MessageBox.Show(
                this,
                "Imaging Manager could not read the image list from the selected WIM.\n\n" + details,
                "Apply WIM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        TemporaryDriveLetterResult? temporaryTargetMount = null;
        string targetRoot;
        bool operationRan = false;
        try
        {
            if (!TryGetPartitionCaptureRoot(partition, out targetRoot))
            {
                UseWaitCursor = true;
                TemporaryDriveLetterResult mountResult;
                try
                {
                    mountResult = _temporaryDriveLetters.Assign(disk.DiskNumber, partition.PartitionNumber);
                }
                finally
                {
                    UseWaitCursor = false;
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
            operationRan = true;
            await RunWimApplyAsync(
                disk,
                partition,
                targetRoot,
                fileSystemResult.FileSystem,
                imagePath,
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

            if (temporaryTargetMount != null || operationRan)
                LoadDisks(disk.DiskNumber);
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
        ImagingDiskInfo disk,
        ImagingPartitionInfo partition,
        string targetRoot,
        string fileSystem,
        string imagePath,
        WimImageInfo image,
        bool configureBootFiles)
    {
        _operationActive = true;
        UpdateSelectedDiskPanel();
        Enabled = false;
        UseWaitCursor = true;

        PartitionFormatResult formatResult;
        try
        {
            formatResult = await _partitionFormatter.FormatQuickAsync(
                disk.DiskNumber,
                partition.PartitionNumber,
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
            UseWaitCursor = false;
        }

        if (!formatResult.Success)
        {
            _operationActive = false;
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
            _operationActive = false;
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
        _operationActive = true;
        UpdateSelectedDiskPanel();
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
            _operationActive = false;
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
        _operationActive = true;
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
            _operationActive = false;
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

        LoadDisks(disk.DiskNumber);
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
