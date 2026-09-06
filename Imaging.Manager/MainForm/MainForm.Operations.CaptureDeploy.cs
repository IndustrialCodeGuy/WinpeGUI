using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
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
                        stageResult = await _winReStaging.StageFromConfiguredRecoveryPartitionAsync(
                            sourceRoot,
                            CancellationToken.None);
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
                                await TryRemoveStagedWinReAsync(sourceRoot);
                                return;
                            }
                        }
                    }
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
                string? removalError = await _temporaryDriveLetters.RemoveAsync(
                    temporarySourceMount,
                    CancellationToken.None);
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
            title: $"Select WIM or split WIM to deploy to Disk {disk.DiskNumber}",
            extension: ".wim;.swm");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM or split WIM file no longer exists.", "Deploy WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            "Imaging Manager could not read the image list from the selected WIM or split WIM.\n\n" + details,
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
        DriveLetterReassignmentResult? displacedCForDeployment = null;
        bool destructiveWorkStarted = false;
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
                        cResult = await _driveLetterReassignment.MoveCToLowestAvailableAsync(
                            AppContext.BaseDirectory,
                            CancellationToken.None,
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
                        displacedCForDeployment = cResult;
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
                    $"The selected WIM or split WIM file is no longer accessible after preparing the deployment drive letters.\n\n{effectiveImagePath}",
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
            string? deploymentDriveCleanupError = null;
            try
            {
                result = await _wimDeployment.DeployAsync(
                    disk,
                    effectiveImagePath,
                    image,
                    firmwareType,
                    windowsDriveLetter,
                    progress,
                    cts.Token,
                    () => destructiveWorkStarted = true);
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
                if (destructiveWorkStarted)
                    deploymentDriveCleanupError = await _wimDeployment.CleanupTemporaryDeploymentDriveLettersAsync(
                        disk.DiskNumber,
                        firmwareType,
                        CancellationToken.None);

                progressDialog.AllowClose();
                progressDialog.Close();
                Enabled = true;
                Activate();
            }

            IReadOnlyList<string> resultWarnings = string.IsNullOrWhiteSpace(deploymentDriveCleanupError)
                ? result.Warnings
                : result.Warnings.Concat(new[]
                {
                    "Temporary deployment drive-letter cleanup was incomplete: " + deploymentDriveCleanupError
                }).ToArray();

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

                if (resultWarnings.Count > 0)
                    details += "\n\nWarnings:\n" + string.Join("\n", resultWarnings.Select(static warning => "- " + warning));

                MessageBox.Show(this, details, "Deploy WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (resultWarnings.Count > 0)
            {
                MessageBox.Show(
                    this,
                    $"The WIM was deployed to Disk {disk.DiskNumber}, but deployment completed with warnings:\n\n" +
                    string.Join("\n", resultWarnings.Select(static warning => "- " + warning)),
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
            if (!destructiveWorkStarted && displacedCForDeployment is { Changed: true })
            {
                string? rollbackError = await _driveLetterReassignment.RollbackMoveCToLowestAvailableAsync(
                    displacedCForDeployment);
                if (!string.IsNullOrWhiteSpace(rollbackError) && !IsDisposed && !Disposing)
                {
                    MessageBox.Show(
                        this,
                        "Deployment did not begin, but Imaging Manager could not fully restore the original C: drive assignment.\n\n" + rollbackError,
                        "Deploy WIM - Drive Letter Rollback",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            if (windowsLetterReservation != null)
                _temporaryDriveLetters.Release(windowsLetterReservation);

            EndOperation();

            if (!IsDisposed && !Disposing)
                await RequestDiskRefreshAsync(disk.DiskNumber);
        }
    }
}
