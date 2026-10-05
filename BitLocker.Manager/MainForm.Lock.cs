using BitLocker.Core;

namespace BitLocker.Manager;

public partial class MainForm
{
    private const uint AccessDenied = 0x80070005;

    private void ExecuteLock(BitLockerVolumeInfo volume, IWin32Window owner)
    {
        // First attempt is deliberately non-destructive. If something has the
        // volume open, let the technician explicitly choose whether to force a
        // dismount instead of silently invalidating open handles.
        BitLockerOperationResult result = _backend.Lock(volume.MountPoint, forceDismount: false);
        if (result.Success)
        {
            LoadVolumes(selectLaunchDrive: false);
            return;
        }

        if (result.ReturnCode == AccessDenied)
        {
            DialogResult choice = MessageBox.Show(
                owner,
                "The drive is currently in use and could not be locked normally.\n\n" +
                "Force dismount and lock the drive? Open files or operations using this drive may be interrupted.",
                "Force Lock Drive",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (choice != DialogResult.Yes)
                return;

            result = _backend.Lock(volume.MountPoint, forceDismount: true);
            if (result.Success)
            {
                LoadVolumes(selectLaunchDrive: false);
                return;
            }
        }

        ShowOperationError(owner, "Lock Drive", result);
    }
}
