using System.Globalization;
using System.Text.RegularExpressions;

namespace Imaging.Core;

public sealed class DismWimBackend
{
    private static readonly Regex ImageInfoFieldRegex = new(@"^(?<field>Index|Name|Description)\s*:\s*(?<value>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MountedImageFieldRegex = new(@"^(?<field>Mount Dir|Image File|Image Index|Mounted Read/Write|Status)\s*:\s*(?<value>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<WimOperationResult> CaptureAsync(
        string sourceRoot,
        string imageFile,
        string imageName,
        string? description,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string captureRoot = ImagingPath.NormalizeDriveRoot(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        if (!Directory.Exists(captureRoot))
            throw new DirectoryNotFoundException($"The capture source is not accessible: {captureRoot}");

        List<string> arguments = new()
        {
            "/Capture-Image",
            $"/ImageFile:{imageFile}",
            $"/CaptureDir:{captureRoot}",
            $"/Name:{imageName}",
            "/Compress:max"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        arguments.Add("/CheckIntegrity");
        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> AppendAsync(
        string sourceRoot,
        string imageFile,
        string imageName,
        string? description,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string captureRoot = ImagingPath.NormalizeDriveRoot(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        if (!Directory.Exists(captureRoot))
            throw new DirectoryNotFoundException($"The capture source is not accessible: {captureRoot}");
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file to append to was not found.", imageFile);

        List<string> arguments = new()
        {
            "/Append-Image",
            $"/ImageFile:{imageFile}",
            $"/CaptureDir:{captureRoot}",
            $"/Name:{imageName}"
        };

        if (!string.IsNullOrWhiteSpace(description))
            arguments.Add($"/Description:{description.Trim()}");

        arguments.Add("/CheckIntegrity");
        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> ApplyAsync(
        string targetRoot,
        string imageFile,
        int imageIndex,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string applyRoot = ImagingPath.NormalizeDriveRoot(targetRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(applyRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);

        if (imageIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), "The WIM image index must be greater than zero.");
        if (!Directory.Exists(applyRoot))
            throw new DirectoryNotFoundException($"The apply target is not accessible: {applyRoot}");
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file was not found.", imageFile);

        string[] arguments =
        {
            "/Apply-Image",
            $"/ImageFile:{imageFile}",
            $"/Index:{imageIndex}",
            $"/ApplyDir:{applyRoot}",
            "/CheckIntegrity"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> MountAsync(
        string imageFile,
        int imageIndex,
        string mountDirectory,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);

        if (imageIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), "The WIM image index must be greater than zero.");
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file was not found.", imageFile);

        string imageFullPath = Path.GetFullPath(imageFile);
        string mountFullPath = Path.GetFullPath(mountDirectory);
        if (!Directory.Exists(mountFullPath))
            throw new DirectoryNotFoundException($"The WIM mount folder is not accessible: {mountFullPath}");
        if (Directory.EnumerateFileSystemEntries(mountFullPath).Any())
            throw new InvalidOperationException("The WIM mount folder must be empty.");

        string[] arguments =
        {
            "/Mount-Image",
            $"/ImageFile:{imageFullPath}",
            $"/Index:{imageIndex}",
            $"/MountDir:{mountFullPath}",
            "/CheckIntegrity"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> CommitAsync(
        string mountDirectory,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);

        string mountFullPath = Path.GetFullPath(mountDirectory);
        string[] arguments =
        {
            "/Commit-Image",
            $"/MountDir:{mountFullPath}",
            "/CheckIntegrity"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> UnmountDiscardAsync(
        string mountDirectory,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);

        string mountFullPath = Path.GetFullPath(mountDirectory);
        string[] arguments =
        {
            "/Unmount-Image",
            $"/MountDir:{mountFullPath}",
            "/Discard"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> RemountAsync(
        string mountDirectory,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountDirectory);

        string mountFullPath = Path.GetFullPath(mountDirectory);
        string[] arguments =
        {
            "/Remount-Image",
            $"/MountDir:{mountFullPath}"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> CleanupMountpointsAsync(
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string[] arguments =
        {
            "/Cleanup-Mountpoints"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public Task<WimOperationResult> AddDriversAsync(
        string imagePath,
        string driverPath,
        bool recurse,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverPath);

        string imageFullPath = Path.GetFullPath(imagePath);
        string driverFullPath = Path.GetFullPath(driverPath);
        if (!Directory.Exists(imageFullPath))
            throw new DirectoryNotFoundException($"The offline Windows image is not accessible: {imageFullPath}");
        if (!Directory.Exists(driverFullPath) && !File.Exists(driverFullPath))
            throw new FileNotFoundException("The driver source was not found.", driverFullPath);

        List<string> arguments = new()
        {
            $"/Image:{imageFullPath}",
            "/Add-Driver",
            $"/Driver:{driverFullPath}"
        };

        if (recurse && Directory.Exists(driverFullPath))
            arguments.Add("/Recurse");

        return RunAsync(arguments, progress, cancellationToken);
    }

    public async Task<WimMountedImageInfoResult> GetMountedImagesAsync(CancellationToken cancellationToken)
    {
        WimOperationResult result = await RunAsync(
            new[]
            {
                "/Get-MountedImageInfo",
                "/English"
            },
            progress: null,
            cancellationToken).ConfigureAwait(false);

        return new WimMountedImageInfoResult
        {
            Success = result.Success,
            Canceled = result.Canceled,
            ExitCode = result.ExitCode,
            Output = result.Output,
            Images = result.Success ? ParseMountedImageInfo(result.Output) : Array.Empty<WimMountedImageInfo>()
        };
    }

    public Task<WimOperationResult> ExportAsync(
        string sourceImageFile,
        int sourceImageIndex,
        string destinationImageFile,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceImageFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationImageFile);

        if (sourceImageIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceImageIndex), "The WIM image index must be greater than zero.");
        if (!File.Exists(sourceImageFile))
            throw new FileNotFoundException("The source WIM file was not found.", sourceImageFile);

        string sourceFullPath = Path.GetFullPath(sourceImageFile);
        string destinationFullPath = Path.GetFullPath(destinationImageFile);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The source and destination WIM files must be different files.");

        string? destinationDirectory = Path.GetDirectoryName(destinationFullPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException($"The export destination folder is not accessible: {destinationDirectory}");

        string[] arguments =
        {
            "/Export-Image",
            $"/SourceImageFile:{sourceFullPath}",
            $"/SourceIndex:{sourceImageIndex}",
            $"/DestinationImageFile:{destinationFullPath}",
            "/Compress:max",
            "/CheckIntegrity"
        };

        return RunAsync(arguments, progress, cancellationToken);
    }

    public async Task<WimImageInfoResult> GetImagesAsync(string imageFile, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFile);
        if (!File.Exists(imageFile))
            throw new FileNotFoundException("The WIM file was not found.", imageFile);

        WimOperationResult result = await RunAsync(
            new[]
            {
                "/Get-ImageInfo",
                $"/ImageFile:{imageFile}",
                "/English"
            },
            progress: null,
            cancellationToken).ConfigureAwait(false);

        return new WimImageInfoResult
        {
            Success = result.Success,
            Canceled = result.Canceled,
            ExitCode = result.ExitCode,
            Output = result.Output,
            Images = result.Success ? ParseImageInfo(result.Output) : Array.Empty<WimImageInfo>()
        };
    }

    private async Task<WimOperationResult> RunAsync(
        IEnumerable<string> arguments,
        IProgress<WimOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        DismProcessResult result = await DismProcessRunner.RunAsync(
            arguments,
            (percentage, message) => progress?.Report(new WimOperationProgress(percentage, message)),
            cancellationToken).ConfigureAwait(false);

        return result.Canceled
            ? WimOperationResult.Cancelled(result.Output)
            : WimOperationResult.Completed(result.ExitCode, result.Output);
    }

    private static IReadOnlyList<WimMountedImageInfo> ParseMountedImageInfo(string output)
    {
        List<WimMountedImageInfo> images = new();
        string mountDirectory = string.Empty;
        string imageFile = string.Empty;
        int imageIndex = 0;
        bool readWrite = false;
        string status = string.Empty;

        void flush()
        {
            if (string.IsNullOrWhiteSpace(mountDirectory))
                return;

            images.Add(new WimMountedImageInfo
            {
                MountDirectory = mountDirectory,
                ImageFile = imageFile,
                ImageIndex = imageIndex,
                ReadWrite = readWrite,
                Status = status
            });

            mountDirectory = string.Empty;
            imageFile = string.Empty;
            imageIndex = 0;
            readWrite = false;
            status = string.Empty;
        }

        using StringReader reader = new(output ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Match match = MountedImageFieldRegex.Match(line.Trim());
            if (!match.Success)
                continue;

            string field = match.Groups["field"].Value;
            string value = match.Groups["value"].Value.Trim();
            if (field.Equals("Mount Dir", StringComparison.OrdinalIgnoreCase))
            {
                flush();
                mountDirectory = value;
            }
            else if (field.Equals("Image File", StringComparison.OrdinalIgnoreCase))
            {
                imageFile = value;
            }
            else if (field.Equals("Image Index", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out imageIndex);
            }
            else if (field.Equals("Mounted Read/Write", StringComparison.OrdinalIgnoreCase))
            {
                readWrite = value.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            else if (field.Equals("Status", StringComparison.OrdinalIgnoreCase))
            {
                status = value;
            }
        }

        flush();
        return images.OrderBy(static image => image.MountDirectory, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<WimImageInfo> ParseImageInfo(string output)
    {
        List<WimImageInfo> images = new();
        int? index = null;
        string name = string.Empty;
        string description = string.Empty;

        void flush()
        {
            if (!index.HasValue)
                return;

            images.Add(new WimImageInfo
            {
                Index = index.Value,
                Name = name,
                Description = description
            });
            index = null;
            name = string.Empty;
            description = string.Empty;
        }

        using StringReader reader = new(output ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            Match match = ImageInfoFieldRegex.Match(line.Trim());
            if (!match.Success)
                continue;

            string field = match.Groups["field"].Value;
            string value = match.Groups["value"].Value.Trim();
            if (field.Equals("Index", StringComparison.OrdinalIgnoreCase))
            {
                flush();
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    index = parsed;
            }
            else if (field.Equals("Name", StringComparison.OrdinalIgnoreCase) && index.HasValue)
            {
                name = value;
            }
            else if (field.Equals("Description", StringComparison.OrdinalIgnoreCase) && index.HasValue)
            {
                description = value;
            }
        }

        flush();
        return images.OrderBy(static image => image.Index).ToArray();
    }

}
