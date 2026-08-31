using Imaging.Core;
using Shared.Shell.Theming;
using System.Diagnostics;

namespace Imaging.Manager;

public partial class MainForm
{
    private ImagingBitLockerVolumeInfo? GetBitLockerVolumeForPartition(ImagingPartitionInfo partition)
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null || partition.DriveLetters.Count == 0)
            return null;

        foreach (string drive in partition.DriveLetters)
        {
            string normalizedDrive = ImagingPath.NormalizeDriveRoot(drive);
            if (normalizedDrive.Length == 0)
                continue;

            ImagingBitLockerVolumeInfo? volume = disk.BitLockerVolumes.FirstOrDefault(v =>
                string.Equals(
                    ImagingPath.NormalizeDriveRoot(v.MountPoint),
                    normalizedDrive,
                    StringComparison.OrdinalIgnoreCase));

            if (volume != null)
                return volume;
        }

        return null;
    }

    private async Task UnlockSelectedPartitionAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        ImagingBitLockerVolumeInfo? volume = partition == null ? null : GetBitLockerVolumeForPartition(partition);
        if (disk == null || partition == null || volume?.IsLocked != true || _operationActive)
            return;

        string unlockPath = Path.Combine(AppContext.BaseDirectory, "BitLocker.Unlock.exe");
        if (!File.Exists(unlockPath))
        {
            MessageBox.Show(
                this,
                $"BitLocker unlock helper was not found:\n{unlockPath}",
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        int diskNumber = disk.DiskNumber;
        int partitionNumber = partition.PartitionNumber;
        _operationActive = true;
        Enabled = false;
        UpdateSelectedDiskPanel();
        _lblStatus.Text = "Unlock window opened.";
        _lblStatus.Visible = true;

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = unlockPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add("--drive");
            startInfo.ArgumentList.Add(volume.MountPoint);

            if (ShellTheme.DarkMode)
                startInfo.ArgumentList.Add("--dark");

            Process? process = Process.Start(startInfo);
            if (process == null)
            {
                MessageBox.Show(
                    this,
                    "Unable to start BitLocker unlock helper.",
                    "Unlock Drive",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (process)
                await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unlock Drive", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            _operationActive = false;
            LoadDisks(diskNumber);
            SelectPartitionByNumber(partitionNumber);
        }
    }
}
