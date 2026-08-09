namespace BitLocker.Core;

// Combines the two backends used by the manager: manage-bde is used for
// status text because it matches the Windows command output, while WMI is used
// for interactive operations that need structured return codes.
public sealed class BitLockerCompositeBackend : IBitLockerBackend
{
    private readonly BitLockerManageBdeBackend _statusBackend = new();
    private readonly BitLockerWmiBackend _operationBackend = new();

    public IReadOnlyList<BitLockerVolumeInfo> GetVolumes()
    {
        return _statusBackend.GetVolumes();
    }

    public BitLockerOperationResult UnlockWithPassphrase(string mountPoint, char[] passphrase)
    {
        return _operationBackend.UnlockWithPassphrase(mountPoint, passphrase);
    }

    public BitLockerOperationResult UnlockWithRecoveryPassword(string mountPoint, char[] recoveryPassword)
    {
        return _operationBackend.UnlockWithRecoveryPassword(mountPoint, recoveryPassword);
    }

    public BitLockerOperationResult UnlockWithRecoveryKeyFile(string mountPoint, string keyFilePath)
    {
        return _statusBackend.UnlockWithRecoveryKeyFile(mountPoint, keyFilePath);
    }

    public BitLockerOperationResult Lock(string mountPoint)
    {
        return _operationBackend.Lock(mountPoint);
    }
}
