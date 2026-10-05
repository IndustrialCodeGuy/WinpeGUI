using System.Text;

namespace BitLocker.Core;

// WMI is authoritative for structured state and interactive operations.
// manage-bde is retained as a best-effort source of familiar human-readable
// status text and for .bek recovery-key-file unlocks.
public sealed class BitLockerCompositeBackend : IBitLockerBackend
{
    private readonly BitLockerManageBdeBackend _statusBackend = new();
    private readonly BitLockerWmiBackend _operationBackend = new();

    public IReadOnlyList<BitLockerVolumeInfo> GetVolumes()
    {
        IReadOnlyList<BitLockerVolumeInfo> structuredVolumes = _operationBackend.GetVolumes();

        if (!string.IsNullOrWhiteSpace(_operationBackend.LastErrorMessage))
        {
            throw new InvalidOperationException(
                $"BitLocker WMI status could not be read. {_operationBackend.LastErrorMessage}");
        }

        Dictionary<string, BitLockerVolumeInfo> displayVolumes = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (BitLockerVolumeInfo volume in _statusBackend.GetVolumes())
                displayVolumes[volume.MountPoint] = volume;
        }
        catch
        {
            // WMI remains sufficient for the manager to operate. If manage-bde
            // cannot provide its text block, build a compact WMI status instead.
        }

        return structuredVolumes
            .Select(volume =>
            {
                displayVolumes.TryGetValue(volume.MountPoint, out BitLockerVolumeInfo? displayVolume);
                return MergeDisplayData(volume, displayVolume);
            })
            .OrderBy(static volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    public BitLockerOperationResult Lock(string mountPoint, bool forceDismount = false)
    {
        return _operationBackend.Lock(mountPoint, forceDismount);
    }

    private static BitLockerVolumeInfo MergeDisplayData(
        BitLockerVolumeInfo structured,
        BitLockerVolumeInfo? display)
    {
        string volumeLabel = string.IsNullOrWhiteSpace(structured.VolumeLabel)
            ? display?.VolumeLabel ?? string.Empty
            : structured.VolumeLabel;

        return new BitLockerVolumeInfo
        {
            MountPoint = structured.MountPoint,
            VolumeLabel = volumeLabel,
            IsLocked = structured.IsLocked,
            IsStatusKnown = structured.IsStatusKnown,
            LockStatusText = structured.LockStatusText,
            ProtectionOn = structured.ProtectionOn,
            ProtectionOff = structured.ProtectionOff,
            IsEncrypted = structured.IsEncrypted,
            HasKeyProtectors = structured.HasKeyProtectors,
            IsBitLockerCapable = structured.IsBitLockerCapable,
            VisualState = structured.VisualState,
            // Keep manage-bde's environment-relative OS/data classification
            // when available so the existing lock-button behavior does not
            // change just because structured state moved to WMI.
            IsSystemVolume = display?.IsSystemVolume ?? structured.IsSystemVolume,
            VolumeTypeText = structured.VolumeTypeText,
            EncryptionMethodText = structured.EncryptionMethodText,
            RecoveryKeyId = structured.RecoveryKeyId,
            KeyProtectors = structured.KeyProtectors,
            ProtectorSummary = structured.ProtectorSummary,
            StatusText = BuildStatusText(structured, display?.StatusText)
        };
    }

    private static string BuildStatusText(BitLockerVolumeInfo volume, string? manageBdeStatusText)
    {
        StringBuilder text = new();

        if (!string.IsNullOrWhiteSpace(manageBdeStatusText))
        {
            text.Append(manageBdeStatusText.TrimEnd());
        }
        else
        {
            text.AppendLine($"Volume {volume.MountPoint.TrimEnd('\\')} [{volume.VolumeLabel}]");
            text.AppendLine($"    Volume Type:       {volume.VolumeTypeText}");
            text.AppendLine($"    Lock Status:       {volume.LockStatusText}");
            text.AppendLine($"    Protection Status: {GetProtectionStatusText(volume)}");
            text.Append($"    Encryption Method: {volume.EncryptionMethodText}");
        }

        if (volume.KeyProtectors.Count > 0)
        {
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("Key Protector IDs");
            text.AppendLine("-----------------");

            foreach (BitLockerKeyProtectorInfo protector in volume.KeyProtectors)
            {
                text.Append(protector.TypeText);

                if (!string.IsNullOrWhiteSpace(protector.FriendlyName))
                    text.Append($" - {protector.FriendlyName}");

                if (!string.IsNullOrWhiteSpace(protector.Id))
                    text.Append($": {protector.Id}");

                text.AppendLine();
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string GetProtectionStatusText(BitLockerVolumeInfo volume)
    {
        if (volume.ProtectionOn)
            return "Protection On";

        if (volume.ProtectionOff)
            return "Protection Off";

        return "Unknown";
    }
}
