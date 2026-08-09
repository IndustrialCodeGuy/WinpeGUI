namespace BitLocker.Core;

// Common backend contract used by the manager and unlock helper.
public interface IBitLockerBackend
{
    IReadOnlyList<BitLockerVolumeInfo> GetVolumes();
    BitLockerOperationResult UnlockWithPassphrase(string mountPoint, char[] passphrase);
    BitLockerOperationResult UnlockWithRecoveryPassword(string mountPoint, char[] recoveryPassword);
    BitLockerOperationResult UnlockWithRecoveryKeyFile(string mountPoint, string keyFilePath);
    BitLockerOperationResult Lock(string mountPoint);
}
