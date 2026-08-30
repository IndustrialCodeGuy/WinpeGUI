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

    private string? RunExplorerPicker(bool save, string title)
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
            startInfo.ArgumentList.Add(".ffu");
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
