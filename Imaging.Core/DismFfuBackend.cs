namespace Imaging.Core;

public sealed class DismFfuBackend
{
    public Task<FfuOperationResult> CaptureAsync(
        ImagingDiskInfo disk,
        string imageFile,
        string imageName,
        string? description,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        List<string> arguments = new()
        {
            "/Capture-FFU",
            $"/ImageFile:{imageFile}",
            $"/CaptureDrive:{GetPhysicalDrivePath(disk)}",
            $"/Name:{imageName}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<FfuOperationResult> ApplyAsync(
        ImagingDiskInfo disk,
        string imageFile,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);

        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The FFU file was not found.", imageFile);

        return RunAsync(
            new[]
            {
                "/Apply-FFU",
                $"/ImageFile:{imageFile}",
                $"/ApplyDrive:{GetPhysicalDrivePath(disk)}"
            },
            progress,
            cancellationToken);
    }

    private async Task<FfuOperationResult> RunAsync(
        IEnumerable<string> arguments,
        IProgress<FfuOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        DismProcessResult result = await DismProcessRunner.RunAsync(
            arguments,
            (percentage, message) => progress?.Report(new FfuOperationProgress(percentage, message)),
            cancellationToken).ConfigureAwait(false);

        return result.Canceled
            ? FfuOperationResult.Cancelled(result.Output)
            : FfuOperationResult.Completed(result.ExitCode, result.Output);
    }

    private static string GetPhysicalDrivePath(ImagingDiskInfo disk)
    {
        if (!string.IsNullOrWhiteSpace(disk.DevicePath) &&
            disk.DevicePath.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase))
        {
            return disk.DevicePath;
        }

        return $@"\\.\PhysicalDrive{disk.DiskNumber}";
    }
}
