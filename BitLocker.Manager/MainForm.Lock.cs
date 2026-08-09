using BitLocker.Core;

namespace BitLocker.Manager;

public partial class MainForm
{
    private void ExecuteLock(BitLockerVolumeInfo volume, IWin32Window owner)
    {
        BitLockerOperationResult result = _backend.Lock(volume.MountPoint);
        if (result.Success)
        {
            LoadVolumes(selectLaunchDrive: false);
            return;
        }

        ShowOperationError(owner, "Lock Drive", result);
    }
}