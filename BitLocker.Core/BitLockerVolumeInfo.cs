namespace BitLocker.Core;

// UI-facing volume snapshot. Some fields are only populated by one backend,
// so callers should treat missing strings as optional display data.
public sealed class BitLockerVolumeInfo
{
    public string MountPoint { get; init; } = string.Empty;
    public string VolumeLabel { get; init; } = string.Empty;
    public bool? IsLocked { get; init; }
    public bool IsStatusKnown { get; init; } = true;
    public string LockStatusText { get; init; } = string.Empty;
    public bool ProtectionOn { get; init; }
    public bool ProtectionOff { get; init; }
    public bool IsEncrypted { get; init; }
    public bool HasKeyProtectors { get; init; }
    public bool IsBitLockerCapable { get; init; }
    public BitLockerVisualState VisualState { get; init; }
    public bool IsProtectionOff => VisualState == BitLockerVisualState.ProtectionOff;
    public bool IsSystemVolume { get; init; }
    public string VolumeTypeText { get; init; } = string.Empty;
    public string RecoveryKeyId { get; init; } = string.Empty;
    public IReadOnlyList<string> ProtectorSummary { get; init; } = Array.Empty<string>();
    public string StatusText { get; init; } = string.Empty;

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(VolumeLabel))
                return MountPoint;

            return $"{VolumeLabel} ({MountPoint.TrimEnd('\\')})";
        }
    }

    public override string ToString() => DisplayName;
}
