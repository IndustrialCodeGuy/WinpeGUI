namespace Imaging.Core;

public enum FfuCaptureSuitability
{
    Ready,
    BitLockerEncrypted,
    BitLockerStatusUnknown
}

public sealed class FfuCaptureAssessment
{
    public FfuCaptureSuitability Suitability { get; init; }
    public IReadOnlyList<ImagingBitLockerVolumeInfo> AffectedVolumes { get; init; } = Array.Empty<ImagingBitLockerVolumeInfo>();

    public bool RequiresEncryptionWarning => Suitability == FfuCaptureSuitability.BitLockerEncrypted;

    public static FfuCaptureAssessment Evaluate(ImagingDiskInfo disk)
    {
        if (!disk.BitLockerStatusAvailable)
        {
            return new FfuCaptureAssessment
            {
                Suitability = FfuCaptureSuitability.BitLockerStatusUnknown,
                AffectedVolumes = Array.Empty<ImagingBitLockerVolumeInfo>()
            };
        }

        ImagingBitLockerVolumeInfo[] affected = disk.BitLockerVolumes
            .Where(static v => v.HasEncryptionRemaining)
            .ToArray();

        if (affected.Length > 0)
        {
            return new FfuCaptureAssessment
            {
                Suitability = FfuCaptureSuitability.BitLockerEncrypted,
                AffectedVolumes = affected
            };
        }

        return new FfuCaptureAssessment
        {
            Suitability = FfuCaptureSuitability.Ready,
            AffectedVolumes = Array.Empty<ImagingBitLockerVolumeInfo>()
        };
    }
}
