using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task ApplyWimToSelectedPartitionAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        if (disk == null || partition == null || _operationActive)
            return;

        string partitionName = GetPartitionDisplayName(partition);
        string? imagePath = RunExplorerPicker(
            save: false,
            title: $"Select WIM or split WIM to apply to {partitionName}",
            extension: ".wim;.swm");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM or split WIM file no longer exists.", "Apply WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        DriveLetterReassignmentResult? movedCForTemporaryTarget = null;
        DriveLetterReassignmentResult? targetCReassignment = null;
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
                    mountResult = await _temporaryDriveLetters.AssignAsync(
                        disk.DiskNumber,
                        partition.PartitionNumber,
                        CancellationToken.None);
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
                        DriveLetterReassignmentResult displacedC = await _driveLetterReassignment.MoveCToLowestAvailableAsync(
                            AppContext.BaseDirectory,
                            CancellationToken.None,
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

                        if (displacedC.Changed)
                            movedCForTemporaryTarget = displacedC;

                        effectiveImagePath = DriveLetterReassignmentService.RebasePathFromDisplacedC(
                            effectiveImagePath,
                            displacedC.DisplacedCRoot);
                    }

                    DriveLetterReassignmentResult cResult = await _driveLetterReassignment.ReassignPartitionToCAsync(
                        disk.DiskNumber,
                        partition.PartitionNumber,
                        targetRoot,
                        AppContext.BaseDirectory,
                        CancellationToken.None);

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

                    targetCReassignment = cResult.Changed ? cResult : null;
                    targetRoot = cResult.TargetRoot;
                    effectiveImagePath = DriveLetterReassignmentService.RebasePathFromDisplacedC(
                        effectiveImagePath,
                        cResult.DisplacedCRoot);
                }
                finally
                {
                    SetWaitCursorState(false);
                }

                if (!File.Exists(effectiveImagePath))
                {
                    MessageBox.Show(
                        this,
                        $"The selected WIM or split WIM file is no longer accessible after reassigning the target to C:.\n\n{effectiveImagePath}",
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
                disk,
                partition,
                targetRoot,
                fileSystemResult.FileSystem,
                effectiveImagePath,
                selectedImage,
                configureBootFiles);
        }
        finally
        {
            List<string> rollbackErrors = new();
            if (!operationRan)
            {
                if (targetCReassignment is { Changed: true })
                {
                    string? rollbackError = await _driveLetterReassignment.RollbackPartitionReassignmentToCAsync(
                        disk.DiskNumber,
                        partition.PartitionNumber,
                        targetCReassignment);
                    if (!string.IsNullOrWhiteSpace(rollbackError))
                        rollbackErrors.Add(rollbackError);
                }

                if (movedCForTemporaryTarget is { Changed: true })
                {
                    string? rollbackError = await _driveLetterReassignment.RollbackMoveCToLowestAvailableAsync(
                        movedCForTemporaryTarget);
                    if (!string.IsNullOrWhiteSpace(rollbackError))
                        rollbackErrors.Add(rollbackError);
                }
            }

            if (temporaryTargetMount != null)
            {
                string? removalError;
                if (operationRan && targetCReassignment is { Changed: true })
                {
                    // The target no longer owns its temporary letter after becoming C:.
                    _temporaryDriveLetters.ReleaseReservation(temporaryTargetMount);
                    removalError = null;
                }
                else
                {
                    removalError = await _temporaryDriveLetters.RemoveAsync(
                        temporaryTargetMount,
                        CancellationToken.None);
                }

                if (!string.IsNullOrWhiteSpace(removalError))
                    rollbackErrors.Add(removalError);
            }

            if (rollbackErrors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Imaging Manager could not fully restore or clean up the pre-operation drive-letter assignments.\n\n" +
                    string.Join("\n\n", rollbackErrors),
                    "Apply WIM - Drive Letter Cleanup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
        ImagingDiskInfo disk,
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
                        disk,
                        partition,
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
            if (!string.IsNullOrWhiteSpace(bootResult.Warning))
                details += "\n\nWarning:\n" + bootResult.Warning;
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
            string bootWarning = bootResult is { Success: true } && !string.IsNullOrWhiteSpace(bootResult.Warning)
                ? "\n\nWarning:\n" + bootResult.Warning
                : string.Empty;
            MessageBox.Show(
                this,
                $"The WIM image was applied successfully to {targetRoot.TrimEnd('\\')}.\n\n" +
                $"Image: {image.DisplayName}" + bootText + bootWarning,
                "Apply WIM",
                MessageBoxButtons.OK,
                string.IsNullOrWhiteSpace(bootWarning) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
