using System.Management;

namespace BitLocker.Core;

public sealed class BitLockerWmiBackend : IBitLockerBackend
{
    // MicrosoftVolumeEncryption is the authoritative source for structured
    // BitLocker state and return codes. manage-bde is retained separately only
    // for familiar human-readable status text and .bek file unlock fallback.
    private const string NamespacePath = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string QueryText = "SELECT * FROM Win32_EncryptableVolume";
    private const string ClassName = "Win32_EncryptableVolume";

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
            using ManagementObject? volume = FindVolumeObject(normalizedMountPoint);
            return volume == null ? null : ReadVolumeInfo(volume);
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            return null;
        }
    }

    public IReadOnlyList<string> GetRecoveryPasswordIdPrefixes(string mountPoint)
    {
        return GetProtectorIdsByType(mountPoint, 3)
            .Select(GetProtectorIdPrefix)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    public IReadOnlyList<string> GetExternalKeyIdPrefixes(string mountPoint)
    {
        return GetProtectorIdsByType(mountPoint, 2)
            .Select(GetProtectorIdPrefix)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    // Kept for callers that only need the first recovery-password ID. External
    // key IDs are intentionally not used as a fallback because they identify a
    // different protector type and correspond to .bek recovery keys.
    public string GetRecoveryKeyIdPrefix(string mountPoint)
    {
        return GetRecoveryPasswordIdPrefixes(mountPoint).FirstOrDefault() ?? string.Empty;
    }

    public IReadOnlyList<string> GetProtectorSummary(string mountPoint)
    {
        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return Array.Empty<string>();

        try
        {
            using ManagementObject? volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null || !TryGetKeyProtectors(volume, out IReadOnlyList<BitLockerKeyProtectorInfo> protectors))
                return Array.Empty<string>();

            return protectors.Select(static protector => protector.DisplayText).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
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

        if (!BitLockerRecoveryPassword.HasCompleteLength(secret))
        {
            return BitLockerOperationResult.Fail(
                0x80310035,
                "The recovery password format is invalid. Enter the 48-digit BitLocker recovery password.");
        }

        if (TryValidateRecoveryPasswordFormat(secret, out bool formatIsValid) && !formatIsValid)
        {
            return BitLockerOperationResult.Fail(
                0x80310035,
                "The recovery password format is invalid. Enter a valid BitLocker recovery password.");
        }

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
            inParams["NumericalPassword"] = BitLockerRecoveryPassword.NormalizeForWmi(secret);

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

    public bool TryValidateRecoveryPasswordFormat(char[] secret, out bool isValid)
    {
        isValid = false;

        if (secret == null || secret.Length == 0)
            return true;

        string normalized = BitLockerRecoveryPassword.NormalizeForWmi(secret);
        if (!BitLockerRecoveryPassword.HasCompleteLength(normalized))
            return true;

        try
        {
            ManagementScope scope = CreateScope();
            using ManagementClass encryptableVolumeClass = new(scope, new ManagementPath(ClassName), null);
            using ManagementBaseObject inParams = encryptableVolumeClass.GetMethodParameters("IsNumericalPasswordValid");
            inParams["NumericalPassword"] = normalized;

            using ManagementBaseObject outParams = encryptableVolumeClass.InvokeMethod(
                "IsNumericalPasswordValid",
                inParams,
                null);

            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) || returnValue != 0)
                return false;

            return TryReadBooleanProperty(outParams, "IsNumericalPasswordValid", out isValid);
        }
        catch
        {
            // Validation is advisory. If this method is unavailable in a given
            // PE build, the actual unlock call still performs authoritative
            // BitLocker validation and returns FVE_E_INVALID_PASSWORD_FORMAT.
            return false;
        }
    }

    public BitLockerOperationResult UnlockWithRecoveryKeyFile(string mountPoint, string keyFilePath)
    {
        BitLockerManageBdeBackend fallback = new();
        return fallback.UnlockWithRecoveryKeyFile(mountPoint, keyFilePath);
    }

    public BitLockerOperationResult Lock(string mountPoint, bool forceDismount = false)
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
            inParams["ForceDismount"] = forceDismount;

            using ManagementBaseObject outParams = volume.InvokeMethod("Lock", inParams, null);
            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue))
            {
                return BitLockerOperationResult.Fail(
                    1,
                    "The BitLocker operation did not return a status code.");
            }

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

    private IReadOnlyList<string> GetProtectorIdsByType(string mountPoint, uint protectorType)
    {
        string? normalizedMountPoint = NormalizeMountPoint(mountPoint);
        if (string.IsNullOrWhiteSpace(normalizedMountPoint))
            return Array.Empty<string>();

        try
        {
            using ManagementObject? volume = FindVolumeObject(normalizedMountPoint);
            if (volume == null)
                return Array.Empty<string>();

            return GetKeyProtectorIds(volume, protectorType);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> GetKeyProtectorIds(ManagementObject volume, uint protectorType)
    {
        using ManagementBaseObject inParams = volume.GetMethodParameters("GetKeyProtectors");
        inParams["KeyProtectorType"] = protectorType;

        using ManagementBaseObject outParams = volume.InvokeMethod("GetKeyProtectors", inParams, null);
        if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) ||
            returnValue != 0)
            return Array.Empty<string>();

        if (outParams["VolumeKeyProtectorID"] is not string[] protectorIds)
            return Array.Empty<string>();

        return protectorIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
    }

    private static bool TryGetKeyProtectors(
        ManagementObject volume,
        out IReadOnlyList<BitLockerKeyProtectorInfo> protectors)
    {
        protectors = Array.Empty<BitLockerKeyProtectorInfo>();

        try
        {
            using ManagementBaseObject inParams = volume.GetMethodParameters("GetKeyProtectors");
            inParams["KeyProtectorType"] = 0u;

            using ManagementBaseObject outParams = volume.InvokeMethod("GetKeyProtectors", inParams, null);
            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) ||
                returnValue != 0)
                return false;

            if (outParams["VolumeKeyProtectorID"] is not string[] protectorIds || protectorIds.Length == 0)
            {
                protectors = Array.Empty<BitLockerKeyProtectorInfo>();
                return true;
            }

            List<BitLockerKeyProtectorInfo> results = new(protectorIds.Length);
            foreach (string protectorId in protectorIds)
            {
                if (string.IsNullOrWhiteSpace(protectorId))
                    continue;

                uint typeCode = GetKeyProtectorType(volume, protectorId);
                results.Add(new BitLockerKeyProtectorInfo
                {
                    Id = protectorId,
                    TypeCode = typeCode,
                    TypeText = MapKeyProtectorType(typeCode),
                    FriendlyName = GetKeyProtectorFriendlyName(volume, protectorId)
                });
            }

            protectors = results;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint GetKeyProtectorType(ManagementObject volume, string protectorId)
    {
        try
        {
            using ManagementBaseObject inParams = volume.GetMethodParameters("GetKeyProtectorType");
            inParams["VolumeKeyProtectorID"] = protectorId;

            using ManagementBaseObject outParams = volume.InvokeMethod("GetKeyProtectorType", inParams, null);
            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) ||
                returnValue != 0)
            {
                return 0;
            }

            return BitLockerWmiReader.TryReadUInt32Property(outParams, "KeyProtectorType", out uint protectorType)
                ? protectorType
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetKeyProtectorFriendlyName(ManagementObject volume, string protectorId)
    {
        try
        {
            using ManagementBaseObject inParams = volume.GetMethodParameters("GetKeyProtectorFriendlyName");
            inParams["VolumeKeyProtectorID"] = protectorId;

            using ManagementBaseObject outParams = volume.InvokeMethod("GetKeyProtectorFriendlyName", inParams, null);
            if (!BitLockerWmiReader.TryReadUInt32Property(outParams, "ReturnValue", out uint returnValue) ||
                returnValue != 0)
            {
                return string.Empty;
            }

            return Convert.ToString(outParams["FriendlyName"])?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CreateSecretString(char[] secret)
    {
        return secret == null || secret.Length == 0
            ? string.Empty
            : new string(secret);
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

        uint volumeType = BitLockerWmiReader.TryReadUInt32Property(volume, "VolumeType", out uint rawVolumeType)
            ? rawVolumeType
            : uint.MaxValue;
        bool isSystemVolume = volumeType == 0;

        uint? lockStatus = GetLockStatus(volume);
        uint? protectionStatus = GetProtectionStatus(volume);
        uint? encryptionMethod = GetEncryptionMethod(volume);

        bool protectorStatusKnown = TryGetKeyProtectors(
            volume,
            out IReadOnlyList<BitLockerKeyProtectorInfo> keyProtectors);

        BitLockerKeyProtectorState keyProtectorState = protectorStatusKnown
            ? keyProtectors.Count > 0
                ? BitLockerKeyProtectorState.Present
                : BitLockerKeyProtectorState.None
            : BitLockerKeyProtectorState.Unknown;

        BitLockerLockState lockState = ToLockState(lockStatus);
        BitLockerResolvedState resolvedState = BitLockerVolumeStateResolver.Resolve(new BitLockerStateInput(
            lockState,
            ToEncryptionState(encryptionMethod),
            ToProtectionState(protectionStatus),
            keyProtectorState));

        string label = GetVolumeLabelSafe(mountPoint);
        string recoveryPasswordId = keyProtectors
            .FirstOrDefault(static protector => protector.TypeCode == 3)?.IdPrefix ?? string.Empty;

        return new BitLockerVolumeInfo
        {
            MountPoint = mountPoint,
            VolumeLabel = label,
            IsLocked = ToNullableBool(lockState),
            IsStatusKnown = resolvedState.IsStatusKnown,
            LockStatusText = MapLockStatus(lockState),
            ProtectionOn = resolvedState.ProtectionOn,
            ProtectionOff = resolvedState.ProtectionOff,
            IsEncrypted = resolvedState.IsEncrypted,
            HasKeyProtectors = resolvedState.HasKeyProtectors,
            IsBitLockerCapable = resolvedState.IsBitLockerVolume,
            VisualState = resolvedState.VisualState,
            IsSystemVolume = isSystemVolume,
            VolumeTypeText = MapVolumeType(volumeType),
            EncryptionMethodText = MapEncryptionMethod(encryptionMethod),
            RecoveryKeyId = recoveryPasswordId,
            KeyProtectors = keyProtectors,
            ProtectorSummary = keyProtectors.Select(static protector => protector.DisplayText).ToArray()
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

    private static bool TryReadBooleanProperty(ManagementBaseObject obj, string propertyName, out bool value)
    {
        value = false;

        try
        {
            object? raw = obj[propertyName];
            if (raw == null)
                return false;

            value = Convert.ToBoolean(raw);
            return true;
        }
        catch
        {
            return false;
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

    private static string MapLockStatus(BitLockerLockState lockState)
    {
        return lockState switch
        {
            BitLockerLockState.Locked => "Locked",
            BitLockerLockState.Unlocked => "Unlocked",
            _ => "Unknown"
        };
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

    private static string MapEncryptionMethod(uint? encryptionMethod)
    {
        return encryptionMethod switch
        {
            null => "Unknown",
            0 => "None",
            1 => "AES 128 with Diffuser",
            2 => "AES 256 with Diffuser",
            3 => "AES 128",
            4 => "AES 256",
            5 => "Hardware Encryption",
            6 => "XTS-AES 128",
            7 => "XTS-AES 256",
            uint.MaxValue => "Unknown",
            _ => $"Unknown ({encryptionMethod.Value})"
        };
    }

    private static string MapKeyProtectorType(uint protectorType)
    {
        return protectorType switch
        {
            1 => "TPM",
            2 => "External Key (.bek)",
            3 => "Recovery Password",
            4 => "TPM + PIN",
            5 => "TPM + Startup Key",
            6 => "TPM + PIN + Startup Key",
            7 => "Public Key / Certificate",
            8 => "Passphrase",
            9 => "TPM Certificate",
            10 => "SID / CNG Protector",
            _ => protectorType == 0 ? "Unknown" : $"Unknown ({protectorType})"
        };
    }

    private static string GetProtectorIdPrefix(string protectorId)
    {
        if (string.IsNullOrWhiteSpace(protectorId))
            return string.Empty;

        string cleaned = protectorId.Trim().Trim('{', '}');
        return cleaned.Length <= 8 ? cleaned : cleaned[..8];
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
