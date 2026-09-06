using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task RunOperationAsync(
        FfuOperationKind kind,
        ImagingDiskInfo disk,
        string imagePath,
        Func<string, IProgress<FfuOperationProgress>, CancellationToken, Task<FfuOperationResult>> operation)
    {
        string operationName = kind == FfuOperationKind.Apply ? "Apply FFU" : "Capture FFU";
        if (!TryBeginOperation(operationName, disk))
            return;

        string operationImagePath = kind == FfuOperationKind.Capture && File.Exists(imagePath)
            ? CreateSiblingTemporaryOutputPath(imagePath)
            : imagePath;

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
            result = await operation(operationImagePath, progress, cts.Token);
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

        if (kind == FfuOperationKind.Capture && result.Success && !result.Canceled &&
            !string.Equals(operationImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryCommitTemporaryOutput(operationImagePath, imagePath, out string? commitError))
            {
                result = new FfuOperationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = "The replacement FFU was captured successfully, but Imaging Manager could not safely replace the existing file.\n\n" + commitError
                };
            }
        }

        if (kind == FfuOperationKind.Capture && (!result.Success || result.Canceled))
            TryDeletePartialCaptureOutput(operationImagePath);

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

    private string? RunExplorerPicker(
        bool save,
        string title,
        string extension,
        string? initialPath = null) =>
        RunExplorerPickerCore(save ? "--savefile" : "--openfile", title, extension, initialPath);

    private string? RunExplorerFolderPicker(string title) =>
        RunExplorerPickerCore("--selectfolder", title, extension: null, initialPath: null);

    private string? RunExplorerPickerCore(
        string mode,
        string title,
        string? extension,
        string? initialPath = null)
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
            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                startInfo.ArgumentList.Add("--initial");
                startInfo.ArgumentList.Add(initialPath);
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
