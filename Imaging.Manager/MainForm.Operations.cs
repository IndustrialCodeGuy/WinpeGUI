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

            using ApplyWimConfirmDialog confirm = new(
                disk,
                partition,
                targetRoot,
                imagePath,
                imageInfo.Images);
            if (confirm.ShowDialog(this) != DialogResult.OK)
                return;

            WimImageInfo selectedImage = confirm.SelectedImage;
            operationRan = true;
            await RunWimApplyAsync(partition, targetRoot, imagePath, selectedImage);
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

    private async Task RunWimApplyAsync(
        ImagingPartitionInfo partition,
        string targetRoot,
        string imagePath,
        WimImageInfo image)
    {
        _operationActive = true;
        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimApplyProgressDialog progressDialog = new(partition, targetRoot, imagePath, image);
        CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.ApplyAsync(targetRoot, imagePath, image.Index, progress, cts.Token);
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
        else
        {
            MessageBox.Show(
                this,
                $"The WIM image was applied successfully to {targetRoot.TrimEnd('\\')}.\n\n" +
                $"Image: {image.DisplayName}",
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

    private string? RunExplorerPicker(bool save, string title, string extension)
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
            startInfo.ArgumentList.Add(save ? "--savefile" : "--openfile");
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(title);
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(extension);
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
