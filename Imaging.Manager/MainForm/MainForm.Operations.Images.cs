using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;
using System.Globalization;

namespace Imaging.Manager;

public partial class MainForm
{
    private async Task ShowImageInfoAsync()
    {
        if (_operationActive)
            return;

        string? imagePath = RunExplorerPicker(
            save: false,
            title: "Select image file",
            extension: ".wim;.swm;.ffu;.vhd;.vhdx");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected image file no longer exists.", "Image Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string imageFullPath;
        try
        {
            imageFullPath = DismWimBackend.ResolvePrimaryImageFile(imagePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Image Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        WimOperationResult result;
        SetWaitCursorState(true);
        try
        {
            result = await _wimBackend.GetImageInfoAsync(imageFullPath, imageIndex: null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result = new WimOperationResult
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

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Image Info Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using ImageInfoDialog info = new(imageFullPath, result.Output);
        info.ShowDialog(this);
    }

    private async Task DeleteWimImageFromMenuAsync()
    {
        if (_operationActive)
            return;

        string? imagePath = RunExplorerPicker(
            save: false,
            title: "Select WIM image",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        if (!File.Exists(imagePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Delete WIM Image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        WimImageInfoResult? imageInfo = await TryLoadWimImageInfoAsync(imagePath, "Delete WIM Image");
        if (imageInfo == null)
            return;

        if (imageInfo.Images.Count <= 1)
        {
            MessageBox.Show(
                this,
                "DISM Delete Image requires a WIM containing more than one image.",
                "Delete WIM Image",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using DeleteWimImageConfirmDialog confirm = new(imagePath, imageInfo.Images);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        await DeleteWimImageAsync(imagePath, confirm.SelectedImage, imageInfo.Images.Count);
    }

    private async Task SplitWimFromMenuAsync()
    {
        if (_operationActive)
            return;

        string? sourcePath = RunExplorerPicker(
            save: false,
            title: "Select WIM to split",
            extension: ".wim");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;

        if (!File.Exists(sourcePath))
        {
            MessageBox.Show(this, "The selected WIM file no longer exists.", "Split WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await SplitWimAsync(sourcePath);
    }

    private async Task<bool> DeleteWimImageAsync(
        string imagePath,
        WimImageInfo image,
        int imageCount)
    {
        if (imageCount <= 1)
        {
            MessageBox.Show(
                this,
                "DISM Delete Image requires a WIM containing more than one image.",
                "Delete WIM Image",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        if (!TryBeginOperation("Delete WIM Image"))
            return false;

        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimServicingProgressDialog progressDialog = new(
            "Delete WIM Image",
            $"Deleting {image.DisplayName}",
            $"WIM File: {imagePath}");
        progressDialog.Show(this);

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.DeleteImageAsync(
                imagePath,
                image.Index,
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
            MessageBox.Show(this, details, "Delete WIM Image Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        MessageBox.Show(
            this,
            $"The WIM image was deleted successfully.\n\nImage: {image.DisplayName}\nWIM File: {imagePath}",
            "Delete WIM Image",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return true;
    }

    private async Task<bool> SplitWimAsync(string sourcePath)
    {
        if (!Path.GetExtension(sourcePath).Equals(".wim", StringComparison.OrdinalIgnoreCase))
            return false;

        string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string initialDestination = Path.Combine(
            sourceDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".swm");
        string? destinationPath = RunExplorerPicker(
            save: true,
            title: "Split WIM to SWM files",
            extension: ".swm",
            initialPath: initialDestination);
        if (string.IsNullOrWhiteSpace(destinationPath))
            return false;

        if (!destinationPath.EndsWith(".swm", StringComparison.OrdinalIgnoreCase))
            destinationPath += ".swm";

        string destinationFullPath;
        try
        {
            destinationFullPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Split WIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        using SplitWimConfirmDialog confirm = new(sourcePath, destinationFullPath);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return false;

        IReadOnlyList<string> existingParts = GetSplitWimFamilyFiles(destinationFullPath);
        bool replacingExistingSet = existingParts.Count > 0;
        if (replacingExistingSet)
        {
            string filesText = existingParts.Count == 1
                ? existingParts[0]
                : $"{existingParts.Count} existing SWM files beginning with:\n{existingParts[0]}";
            DialogResult replace = MessageBox.Show(
                this,
                $"A split WIM set already exists at this destination.\n\n{filesText}\n\nReplace the existing set?",
                "Split WIM",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes)
                return false;

        }

        if (!TryBeginOperation("Split WIM"))
            return false;

        UpdateSelectedDiskPanel();
        Enabled = false;

        using WimSplitProgressDialog progressDialog = new(sourcePath, destinationFullPath);
        using CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        string operationDestinationPath = replacingExistingSet
            ? CreateSiblingTemporaryOutputPath(destinationFullPath)
            : destinationFullPath;

        Progress<WimOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        WimOperationResult result;
        try
        {
            result = await _wimBackend.SplitAsync(
                sourcePath,
                operationDestinationPath,
                confirm.FileSizeMb,
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
            Activate();
        }

        if (result.Success && !result.Canceled && replacingExistingSet)
        {
            if (!TryCommitSplitWimFamily(operationDestinationPath, destinationFullPath, out string? commitError))
            {
                result = new WimOperationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = "The replacement SWM set was created successfully, but Imaging Manager could not safely replace the existing set.\n\n" + commitError
                };
            }
        }

        if (!result.Success || result.Canceled)
            _ = TryDeleteSplitWimFamily(operationDestinationPath, out _);

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                "The WIM split operation was canceled. Partial SWM files were removed.",
                "Split WIM Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output)
                ? $"DISM exited with code {result.ExitCode}."
                : result.Output;
            MessageBox.Show(this, details, "Split WIM Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        IReadOnlyList<string> parts = GetSplitWimFamilyFiles(destinationFullPath);
        MessageBox.Show(
            this,
            $"The WIM was split successfully.\n\nParts: {parts.Count}\nFirst SWM: {destinationFullPath}\nMaximum requested part size: {confirm.FileSizeMb:N0} MB",
            "Split WIM",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return true;
    }

    private static IReadOnlyList<string> GetSplitWimFamilyFiles(string firstSwmPath)
    {
        string fullPath = Path.GetFullPath(firstSwmPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return Array.Empty<string>();

        string stem = Path.GetFileNameWithoutExtension(fullPath);
        List<string> files = new();
        foreach (string file in Directory.EnumerateFiles(directory, stem + "*.swm", SearchOption.TopDirectoryOnly))
        {
            string candidateStem = Path.GetFileNameWithoutExtension(file);
            if (candidateStem.Equals(stem, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
                continue;
            }

            if (!candidateStem.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = candidateStem[stem.Length..];
            if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int partNumber) && partNumber >= 2)
                files.Add(file);
        }

        return files
            .OrderBy(file => GetSplitWimPartNumber(fullPath, file), Comparer<int>.Default)
            .ThenBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetSplitWimPartNumber(string firstSwmPath, string path)
    {
        string familyStem = Path.GetFileNameWithoutExtension(firstSwmPath);
        string candidateStem = Path.GetFileNameWithoutExtension(path);
        if (candidateStem.Equals(familyStem, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (!candidateStem.StartsWith(familyStem, StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;

        string suffix = candidateStem[familyStem.Length..];
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int part) && part >= 2
            ? part
            : int.MaxValue;
    }

    private static bool TryDeleteSplitWimFamily(string firstSwmPath, out string? error)
    {
        try
        {
            foreach (string file in GetSplitWimFamilyFiles(firstSwmPath))
                File.Delete(file);

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string CreateSiblingTemporaryOutputPath(string destinationPath)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The image destination folder could not be determined.");

        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);
        return Path.Combine(directory, $"~imaging-{Guid.NewGuid():N}-{stem}{extension}");
    }

    private static bool TryCommitTemporaryOutput(string temporaryPath, string destinationPath, out string? error)
    {
        try
        {
            if (!File.Exists(temporaryPath))
                throw new FileNotFoundException("The completed temporary image file was not found.", temporaryPath);
            if (new FileInfo(temporaryPath).Length == 0)
                throw new InvalidDataException("The completed temporary image file is empty.");

            File.Move(temporaryPath, destinationPath, overwrite: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetSplitWimPartPath(string firstSwmPath, int partNumber)
    {
        string fullPath = Path.GetFullPath(firstSwmPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The split WIM destination folder could not be determined.");

        string stem = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, partNumber <= 1 ? stem + ".swm" : stem + partNumber.ToString(CultureInfo.InvariantCulture) + ".swm");
    }

    private static bool TryCommitSplitWimFamily(
        string temporaryFirstSwmPath,
        string destinationFirstSwmPath,
        out string? error)
    {
        IReadOnlyList<string> temporaryParts = GetSplitWimFamilyFiles(temporaryFirstSwmPath);
        if (temporaryParts.Count == 0)
        {
            error = "DISM reported success, but no completed SWM files were found.";
            return false;
        }

        if (temporaryParts.Any(static file => !File.Exists(file) || new FileInfo(file).Length == 0))
        {
            error = "One or more completed SWM files are missing or empty.";
            return false;
        }

        IReadOnlyList<string> existingParts = GetSplitWimFamilyFiles(destinationFirstSwmPath);
        string backupFirstSwmPath = CreateSiblingTemporaryOutputPath(destinationFirstSwmPath);
        List<(string Original, string Backup)> backups = new();
        List<string> installedNewParts = new();

        try
        {
            foreach (string existingPart in existingParts)
            {
                int partNumber = GetSplitWimPartNumber(destinationFirstSwmPath, existingPart);
                string backupPath = GetSplitWimPartPath(backupFirstSwmPath, partNumber);
                File.Move(existingPart, backupPath);
                backups.Add((existingPart, backupPath));
            }
        }
        catch (Exception ex)
        {
            foreach (var (original, backup) in backups.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(backup))
                        File.Move(backup, original, overwrite: true);
                }
                catch
                {
                }
            }

            error = "The existing SWM set could not be staged safely for replacement. The previous set was retained where possible.\n\n" + ex.Message;
            return false;
        }

        try
        {
            foreach (string temporaryPart in temporaryParts)
            {
                int partNumber = GetSplitWimPartNumber(temporaryFirstSwmPath, temporaryPart);
                string destinationPart = GetSplitWimPartPath(destinationFirstSwmPath, partNumber);
                File.Move(temporaryPart, destinationPart);
                installedNewParts.Add(destinationPart);
            }
        }
        catch (Exception ex)
        {
            foreach (string installed in installedNewParts)
            {
                try { if (File.Exists(installed)) File.Delete(installed); } catch { }
            }

            List<string> restoreErrors = new();
            foreach (var (original, backup) in backups)
            {
                try
                {
                    if (File.Exists(backup))
                        File.Move(backup, original, overwrite: true);
                }
                catch (Exception restoreEx)
                {
                    restoreErrors.Add(restoreEx.Message);
                }
            }

            error = "The new SWM set could not be installed. Imaging Manager attempted to restore the previous set.\n\n" + ex.Message;
            if (restoreErrors.Count > 0)
                error += "\n\nRollback errors:\n" + string.Join("\n", restoreErrors);
            return false;
        }

        foreach (var (_, backup) in backups)
        {
            try { if (File.Exists(backup)) File.Delete(backup); } catch { }
        }

        error = null;
        return true;
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

        string operationDestinationPath = File.Exists(destinationPath)
            ? CreateSiblingTemporaryOutputPath(destinationPath)
            : destinationPath;

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
                operationDestinationPath,
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
            Activate();
        }

        if (result.Success && !result.Canceled && !string.Equals(operationDestinationPath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryCommitTemporaryOutput(operationDestinationPath, destinationPath, out string? commitError))
            {
                result = new WimOperationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = "The exported WIM was created successfully, but the existing destination could not be safely replaced.\n\n" + commitError
                };
            }
        }

        if (!result.Success || result.Canceled)
            TryDeletePartialCaptureOutput(operationDestinationPath);

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
}
