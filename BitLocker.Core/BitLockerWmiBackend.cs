using System.Management;
using System.Text;

namespace BitLocker.Core;

public sealed class BitLockerWmiBackend : IBitLockerBackend
{
    // MicrosoftVolumeEncryption provides structured status and return codes for
    // interactive BitLocker operations. The manager still uses manage-bde for
    // the detailed status text shown to the user.
    private const string NamespacePath = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string QueryText = "SELECT * FROM Win32_EncryptableVolume";
    public string? LastErrorMessage { get; private set; }

    // Query methods

    public IReadOnlyList<BitLockerVolumeInfo> GetVolumes()
    {
        LastErrorMessage = null;
        List<BitLockerVolumeInfo> volumes = new();

        try
        {
            ManagementScope scope = CreateScope();
            ObjectQuery query = new(QueryText);
            using ManagementObjectSearcher searcher = new(scope, query);
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementObject volume in results.Cast<ManagementObject>())
            {
                using (volume)
                {
                    BitLockerVolumeInfo? info = ReadVolumeInfo(volume);
                    if (info != null)
                        volumes.Add(info);
                }
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }

        return volumes
            .OrderBy(static v => v.MountPoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public BitLockerVolumeInfo? GetVolume(string mountPoint)
    {
        LastErrorMessage = null;
        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return null;

        try
        {
            ManagementScope scope = CreateScope();
            ObjectQuery query = new(QueryText);
            using ManagementObjectSearcher searcher = new(scope, query);
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementObject volume in results.Cast<ManagementObject>())
            {
                using (volume)
                {
                    string driveLetter = Convert.ToString(volume["DriveLetter"]) ?? string.Empty;
                    string? normalizedDriveLetter = NormalizeMountPoint(driveLetter);

                    if (!string.Equals(normalizedDriveLetter, normalizedMountPoint, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return ReadVolumeInfo(volume);
                }
            }
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
        }

        return null;
    }

    // Operation methods

    public BitLockerOperationResult UnlockWithPassphrase(string mountPoint, char[] secret)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return BitLockerOperationResult.Fail(1, "A drive must be selected.");

        if (secret == null || secret.Length == 0)
            return BitLockerOperationResult.Fail(1, "A password is required.");

        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return BitLockerOperationResult.Fail(1, "Invalid drive path.");

        ManagementObject? volume = null;

        try
        {
            volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null)
                return BitLockerOperationResult.Fail(1, "The selected BitLocker volume was not found.");

            using ManagementBaseObject inParams = volume.GetMethodParameters("UnlockWithPassphrase");
            inParams["Passphrase"] = CreateSecretString(secret);

            using ManagementBaseObject outParams = volume.InvokeMethod("UnlockWithPassphrase", inParams, null);

            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue))
            {
                return BitLockerOperationResult.Fail(
                    1,
                    "The BitLocker operation did not return a status code.");
            }

            if (returnValue == 0)
                return BitLockerOperationResult.Ok("The drive was unlocked successfully.");

            return BitLockerOperationResult.Fail(
                returnValue,
                MapUnlockPassphraseError(returnValue));
        }
        catch (ManagementException ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        catch (Exception ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        finally
        {
            volume?.Dispose();
        }
    }

    public BitLockerOperationResult UnlockWithRecoveryPassword(string mountPoint, char[] secret)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return BitLockerOperationResult.Fail(1, "A drive must be selected.");

        if (secret == null || secret.Length == 0)
            return BitLockerOperationResult.Fail(1, "A recovery password is required.");

        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return BitLockerOperationResult.Fail(1, "Invalid drive path.");

        ManagementObject? volume = null;

        try
        {
            volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null)
                return BitLockerOperationResult.Fail(1, "The selected BitLocker volume was not found.");

            using ManagementBaseObject inParams = volume.GetMethodParameters("UnlockWithNumericalPassword");
            inParams["NumericalPassword"] = NormalizeRecoveryPassword(secret);

            using ManagementBaseObject outParams = volume.InvokeMethod("UnlockWithNumericalPassword", inParams, null);

            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue))
            {
                return BitLockerOperationResult.Fail(
                    1,
                    "The BitLocker operation did not return a status code.");
            }

            if (returnValue == 0)
                return BitLockerOperationResult.Ok("The drive was unlocked successfully.");

            return BitLockerOperationResult.Fail(
                returnValue,
                MapUnlockRecoveryPasswordError(returnValue));
        }
        catch (ManagementException ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        catch (Exception ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        finally
        {
            volume?.Dispose();
        }
    }

    public BitLockerOperationResult UnlockWithRecoveryKeyFile(string mountPoint, string keyFilePath)
    {
        BitLockerManageBdeBackend fallback = new();
        return fallback.UnlockWithRecoveryKeyFile(mountPoint, keyFilePath);
    }

    public BitLockerOperationResult Lock(string mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return BitLockerOperationResult.Fail(1, "A drive must be selected.");

        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return BitLockerOperationResult.Fail(1, "Invalid drive path.");

        ManagementObject? volume = null;

        try
        {
            volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null)
                return BitLockerOperationResult.Fail(1, "The selected BitLocker volume was not found.");

            using ManagementBaseObject inParams = volume.GetMethodParameters("Lock");
            inParams["ForceDismount"] = true;

            using ManagementBaseObject outParams = volume.InvokeMethod("Lock", inParams, null);
            uint returnValue = ReadUInt32Property(outParams, "ReturnValue");

            if (returnValue == 0)
                return BitLockerOperationResult.Ok("The drive was locked successfully.");

            return BitLockerOperationResult.Fail(returnValue, MapLockError(returnValue));
        }
        catch (ManagementException ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        catch (Exception ex)
        {
            return BitLockerOperationResult.Fail(1, ex.Message);
        }
        finally
        {
            volume?.Dispose();
        }
    }

    public string GetRecoveryKeyIdPrefix(string mountPoint)
    {
        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return string.Empty;

        ManagementObject? volume = null;

        try
        {
            volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null)
                return string.Empty;

            string? protectorId = GetFirstKeyProtectorId(volume, 3)
                ?? GetFirstKeyProtectorId(volume, 2);

            if (string.IsNullOrWhiteSpace(protectorId))
                return string.Empty;

            string cleaned = protectorId.Trim().Trim('{', '}');
            return cleaned.Length <= 8 ? cleaned : cleaned[..8];
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            volume?.Dispose();
        }
    }

    public IReadOnlyList<string> GetProtectorSummary(string mountPoint) => Array.Empty<string>();


    // WMI lookup and value helpers

    private ManagementObject? FindVolumeObject(string mountPoint)
    {
        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return null;

        ManagementScope scope = CreateScope();
        ObjectQuery query = new(QueryText);
        using ManagementObjectSearcher searcher = new(scope, query);
        using ManagementObjectCollection results = searcher.Get();

        foreach (ManagementObject volume in results.Cast<ManagementObject>())
        {
            string driveLetter = Convert.ToString(volume["DriveLetter"]) ?? string.Empty;
            string? normalizedDriveLetter = NormalizeMountPoint(driveLetter);

            if (string.Equals(normalizedDriveLetter, normalizedMountPoint, StringComparison.OrdinalIgnoreCase))
                return volume;

            volume.Dispose();
        }

        return null;
    }

    private static string CreateSecretString(char[] secret)
    {
        return secret == null || secret.Length == 0
            ? string.Empty
            : new string(secret);
    }

    private static string NormalizeRecoveryPassword(char[] secret)
    {
        if (secret == null || secret.Length == 0)
            return string.Empty;

        StringBuilder sb = new(secret.Length);

        foreach (char c in secret)
        {
            if (char.IsDigit(c) || c == '-' || c == ' ')
                sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    private static string MapUnlockPassphraseError(uint returnValue)
    {
        return returnValue switch
        {
            0x80310008 => "BitLocker is not enabled on this volume.",
            0x8031006C => "Policy prevents using a passphrase on this volume.",
            0x80310080 => "The password does not meet the required length rules.",
            0x80310081 => "The password does not meet the required complexity rules.",
            0x80310027 => "The password could not unlock the drive.",
            0x80310033 => "This drive does not have a matching passphrase protector.",
            _ => "The drive could not be unlocked with the provided password."
        };
    }

    private static string MapUnlockRecoveryPasswordError(uint returnValue)
    {
        return returnValue switch
        {
            0x80310008 => "BitLocker is not enabled on this volume.",
            0x80310033 => "This drive does not have a recovery password protector.",
            0x80310027 => "The recovery password could not unlock the drive.",
            0x80310035 => "The recovery password format is invalid.",
            _ => "The drive could not be unlocked with the provided recovery password."
        };
    }

    private static string MapLockError(uint returnValue)
    {
        return returnValue switch
        {
            0x80070005 => "Applications are currently accessing this volume.",
            0x80310001 => "BitLocker is not enabled on this volume.",
            0x80310021 => "Protection is disabled on this volume, so it cannot be locked.",
            0x80310022 => "The volume needs a recovery password or external key protector before it can be locked.",
            0x803100B5 => "The operating system volume cannot be locked while Windows is running.",
            _ => "The drive could not be locked."
        };
    }

    private static string? GetFirstKeyProtectorId(ManagementObject volume, uint protectorType)
    {
        using ManagementBaseObject inParams = volume.GetMethodParameters("GetKeyProtectors");
        inParams["KeyProtectorType"] = protectorType;

        using ManagementBaseObject outParams = volume.InvokeMethod("GetKeyProtectors", inParams, null);
        uint returnValue = ReadUInt32Property(outParams, "ReturnValue");
        if (returnValue != 0)
            return null;

        if (outParams["VolumeKeyProtectorID"] is string[] protectorIds)
            return protectorIds.FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));

        return null;
    }

    // WMI plumbing

    private static ManagementScope CreateScope()
    {
        ManagementScope scope = new(NamespacePath);
        scope.Connect();
        return scope;
    }

    private BitLockerVolumeInfo? ReadVolumeInfo(ManagementObject volume)
    {
        string driveLetter = Convert.ToString(volume["DriveLetter"]) ?? string.Empty;
        string? mountPoint = NormalizeMountPoint(driveLetter);

        if (string.IsNullOrWhiteSpace(mountPoint))
            return null;

        uint volumeType = ReadUInt32Property(volume, "VolumeType");
        bool isSystemVolume = volumeType == 0;

        uint? lockStatus = GetLockStatus(volume);
        uint? protectionStatus = GetProtectionStatus(volume);
        uint? encryptionMethod = GetEncryptionMethod(volume);

        BitLockerLockState lockState = ToLockState(lockStatus);
        BitLockerResolvedState resolvedState = BitLockerVolumeStateResolver.Resolve(new BitLockerStateInput(
            lockState,
            ToEncryptionState(encryptionMethod),
            ToProtectionState(protectionStatus)));

        string label = GetVolumeLabelSafe(mountPoint);

        return new BitLockerVolumeInfo
        {
            MountPoint = mountPoint,
            VolumeLabel = label,
            IsLocked = ToNullableBool(lockState),
            IsStatusKnown = resolvedState.IsStatusKnown,
            LockStatusText = lockState == BitLockerLockState.Locked ? "Locked" : lockState == BitLockerLockState.Unlocked ? "Unlocked" : "Unknown",
            ProtectionOn = resolvedState.ProtectionOn,
            ProtectionOff = resolvedState.ProtectionOff,
            IsEncrypted = resolvedState.IsEncrypted,
            HasKeyProtectors = resolvedState.HasKeyProtectors,
            IsBitLockerCapable = resolvedState.IsBitLockerVolume,
            VisualState = resolvedState.VisualState,
            IsSystemVolume = isSystemVolume,
            VolumeTypeText = MapVolumeType(volumeType),
            RecoveryKeyId = string.Empty,
            ProtectorSummary = Array.Empty<string>()
        };
    }

    private static uint? GetLockStatus(ManagementObject volume)
    {
        return BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetLockStatus",
            "LockStatus");
    }

    private static uint? GetProtectionStatus(ManagementObject volume)
    {
        return BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetProtectionStatus",
            "ProtectionStatus");
    }

    private static uint? GetEncryptionMethod(ManagementObject volume)
    {
        return BitLockerWmiReader.TryReadSingleUInt32OutParam(
            volume,
            "GetEncryptionMethod",
            "EncryptionMethod");
    }

    private static BitLockerLockState ToLockState(uint? lockStatus)
    {
        return lockStatus switch
        {
            1 => BitLockerLockState.Locked,
            0 => BitLockerLockState.Unlocked,
            _ => BitLockerLockState.Unknown
        };
    }

    private static BitLockerProtectionState ToProtectionState(uint? protectionStatus)
    {
        return protectionStatus switch
        {
            1 => BitLockerProtectionState.On,
            0 => BitLockerProtectionState.Off,
            _ => BitLockerProtectionState.Unknown
        };
    }

    private static BitLockerEncryptionState ToEncryptionState(uint? encryptionMethod)
    {
        return encryptionMethod switch
        {
            null => BitLockerEncryptionState.Unknown,
            0 => BitLockerEncryptionState.NotEncrypted,
            uint.MaxValue => BitLockerEncryptionState.Unknown,
            _ => BitLockerEncryptionState.Encrypted
        };
    }

    private static bool? ToNullableBool(BitLockerLockState lockState)
    {
        return lockState switch
        {
            BitLockerLockState.Locked => true,
            BitLockerLockState.Unlocked => false,
            _ => null
        };
    }

    private static uint ReadUInt32Property(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            object? value = obj[propertyName];
            if (value == null)
                return 0;

            return Convert.ToUInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static string GetVolumeLabelSafe(string mountPoint)
    {
        try
        {
            DriveInfo drive = new(mountPoint);
            if (!drive.IsReady)
                return string.Empty;

            return drive.VolumeLabel ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string MapVolumeType(uint volumeType)
    {
        return volumeType switch
        {
            0 => "System",
            1 => "Fixed",
            2 => "Removable",
            _ => "Unknown"
        };
    }

    private static string? NormalizeMountPoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + @"\";

        return trimmed;
    }
}
