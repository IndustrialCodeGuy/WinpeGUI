using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
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

        string operationImagePath = !appendToExistingWim && File.Exists(imagePath)
            ? CreateSiblingTemporaryOutputPath(imagePath)
            : imagePath;

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        string cleanupError = string.Empty;
        try
        {
            result = appendToExistingWim
                ? await _wimBackend.AppendAsync(sourceRoot, imagePath, imageName, description, progress, cts.Token)
                : await _wimBackend.CaptureAsync(sourceRoot, operationImagePath, imageName, description, progress, cts.Token);
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
                    await _winReStaging.RemoveStagedWinReAsync(
                        sourceRoot,
                        CancellationToken.None);
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

        if (result.Success && !result.Canceled && !appendToExistingWim &&
            !string.Equals(operationImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryCommitTemporaryOutput(operationImagePath, imagePath, out string? commitError))
            {
                result = new WimOperationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = "The replacement WIM was captured successfully, but Imaging Manager could not safely replace the existing file.\n\n" + commitError
                };
            }
        }

        if ((!result.Success || result.Canceled) && !appendToExistingWim)
            TryDeletePartialCaptureOutput(operationImagePath);

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

    private async Task TryRemoveStagedWinReAsync(string sourceRoot)
    {
        try
        {
            await _winReStaging.RemoveStagedWinReAsync(
                sourceRoot,
                CancellationToken.None);
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
}
