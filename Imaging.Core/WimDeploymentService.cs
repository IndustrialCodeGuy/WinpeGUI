using System.Runtime.InteropServices;
using System.Text;

namespace Imaging.Core;

public sealed class WimDeploymentService
{
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const int SystemPartitionSizeMb = 300;
    public const int RecoveryPartitionSizeMb = 1024;

    private readonly DismWimBackend _wimBackend;
    private readonly TemporaryDriveLetterService _temporaryDriveLetters;

    public WimDeploymentService(
        DismWimBackend wimBackend,
        TemporaryDriveLetterService temporaryDriveLetters)
    {
        _wimBackend = wimBackend ?? throw new ArgumentNullException(nameof(wimBackend));
        _temporaryDriveLetters = temporaryDriveLetters ?? throw new ArgumentNullException(nameof(temporaryDriveLetters));
    }

    public WimDeploymentFirmwareType DetectFirmwareType()
    {
        try
        {
            if (GetFirmwareType(out FirmwareType nativeType))
            {
                return nativeType switch
                {
                    FirmwareType.Bios => WimDeploymentFirmwareType.Bios,
                    FirmwareType.Uefi => WimDeploymentFirmwareType.Uefi,
                    _ => WimDeploymentFirmwareType.Unknown
                };
            }
        }
        catch
        {
        }

        return WimDeploymentFirmwareType.Unknown;
    }

    public async Task<WimBootConfigurationResult> ConfigureAppliedWindowsBootAsync(
        ImagingDiskInfo targetDisk,
        ImagingPartitionInfo targetPartition,
        string windowsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetDisk);
        ArgumentNullException.ThrowIfNull(targetPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);

        string windowsFullPath = Path.GetFullPath(windowsDirectory).TrimEnd('\\');
        if (!Directory.Exists(windowsFullPath))
        {
            return new WimBootConfigurationResult
            {
                Success = false,
                ExitCode = -1,
                Output = $"The applied Windows directory was not found: {windowsFullPath}"
            };
        }

        ImagingDiskInfo currentTargetDisk = targetDisk;
        try
        {
            ImagingInventorySnapshot refreshed = await Task.Run(
                () => new DiskInventory().GetInventory(),
                cancellationToken).ConfigureAwait(false);
            currentTargetDisk = refreshed.Disks.FirstOrDefault(disk => disk.DiskNumber == targetDisk.DiskNumber)
                ?? targetDisk;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to the caller's snapshot. The selected target partition is
            // still handled from its current Windows path below.
        }

        if (!TryFindSystemPartition(currentTargetDisk, out ImagingPartitionInfo systemPartition, out string firmwareType, out string findError))
        {
            return new WimBootConfigurationResult
            {
                Success = false,
                ExitCode = -1,
                Output = findError
            };
        }

        TemporaryDriveLetterResult? temporarySystemMount = null;
        string systemRoot;
        if (systemPartition.PartitionNumber == targetPartition.PartitionNumber)
        {
            string? windowsRoot = ImagingPath.TryGetDriveRootForPath(windowsFullPath);
            systemRoot = ImagingPath.NormalizeDriveRoot(windowsRoot);
        }
        else
        {
            systemRoot = systemPartition.DriveLetters
                .Select(ImagingPath.NormalizeDriveRoot)
                .FirstOrDefault(static root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                ?? string.Empty;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(systemRoot))
            {
                temporarySystemMount = await _temporaryDriveLetters.AssignAsync(
                    currentTargetDisk.DiskNumber,
                    systemPartition.PartitionNumber,
                    cancellationToken).ConfigureAwait(false);
                if (!temporarySystemMount.Success)
                {
                    return new WimBootConfigurationResult
                    {
                        Success = false,
                        ExitCode = -1,
                        Output =
                            $"The target disk's {firmwareType} system partition could not be assigned a temporary drive letter.\n\n" +
                            temporarySystemMount.Error
                    };
                }

                systemRoot = temporarySystemMount.Root;
            }

            string bcdBoot = ResolveAppliedOrSystemTool(windowsFullPath, "bcdboot.exe");
            ProcessExecutionResult result = await ProcessExecutionRunner.RunAsync(
                bcdBoot,
                new[]
                {
                    windowsFullPath,
                    "/s",
                    systemRoot.TrimEnd('\\'),
                    "/f",
                    firmwareType
                },
                cancellationToken).ConfigureAwait(false);

            string cleanupWarning = string.Empty;
            if (temporarySystemMount != null)
            {
                string? cleanupError = await _temporaryDriveLetters.RemoveAsync(
                    temporarySystemMount,
                    CancellationToken.None).ConfigureAwait(false);
                temporarySystemMount = null;
                if (!string.IsNullOrWhiteSpace(cleanupError))
                    cleanupWarning = "Windows boot files were configured, but the temporary system-partition drive letter could not be removed.\n\n" + cleanupError;
            }

            return new WimBootConfigurationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                Output = result.CombinedOutput,
                Warning = cleanupWarning
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WimBootConfigurationResult
            {
                Success = false,
                ExitCode = -1,
                Output = ex.Message
            };
        }
        finally
        {
            if (temporarySystemMount != null)
                _ = await _temporaryDriveLetters.RemoveAsync(
                    temporarySystemMount,
                    CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool TryFindSystemPartition(
        ImagingDiskInfo disk,
        out ImagingPartitionInfo systemPartition,
        out string firmwareType,
        out string error)
    {
        ImagingPartitionInfo? efi = disk.Partitions.FirstOrDefault(static partition =>
            partition.StorageInfo?.GptType.StartsWith("EFI System", StringComparison.OrdinalIgnoreCase) == true ||
            partition.Type.Contains("GPT: System", StringComparison.OrdinalIgnoreCase) ||
            partition.Type.Contains("EFI System", StringComparison.OrdinalIgnoreCase));
        if (efi != null)
        {
            systemPartition = efi;
            firmwareType = "UEFI";
            error = string.Empty;
            return true;
        }

        bool looksGpt = string.Equals(disk.StorageInfo?.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase) ||
                        disk.Partitions.Any(static partition =>
                            partition.Type.StartsWith("GPT:", StringComparison.OrdinalIgnoreCase) ||
                            !string.IsNullOrWhiteSpace(partition.StorageInfo?.GptType));

        ImagingPartitionInfo? active = looksGpt
            ? null
            : disk.Partitions.FirstOrDefault(static partition =>
                partition.StorageInfo?.IsActive == true || partition.BootPartition);
        if (active != null)
        {
            systemPartition = active;
            firmwareType = "BIOS";
            error = string.Empty;
            return true;
        }

        systemPartition = null!;
        firmwareType = string.Empty;
        error =
            $"Imaging Manager could not identify a boot system partition on Disk {disk.DiskNumber}. " +
            "For GPT disks, an EFI System partition is required. For MBR disks, an active system partition is required. " +
            "Boot files were not written to another disk.";
        return false;
    }

    public async Task<WimDeploymentResult> DeployAsync(
        ImagingDiskInfo disk,
        string imagePath,
        WimImageInfo image,
        WimDeploymentFirmwareType firmwareType,
        char windowsDriveLetter,
        IProgress<WimDeploymentProgress>? progress,
        CancellationToken cancellationToken,
        Action? destructiveOperationStarting = null)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(image);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("The WIM or split WIM file was not found.", imagePath);
        if (firmwareType is not (WimDeploymentFirmwareType.Bios or WimDeploymentFirmwareType.Uefi))
            throw new InvalidOperationException("The current firmware mode could not be determined as BIOS or UEFI.");

        windowsDriveLetter = char.ToUpperInvariant(windowsDriveLetter);
        if (windowsDriveLetter is < 'C' or > 'Z' || windowsDriveLetter is 'R' or 'S' or 'X')
            throw new ArgumentOutOfRangeException(nameof(windowsDriveLetter), "The deployment Windows drive letter must be C:-Z: and cannot be R:, S:, or X:.");

        string windowsRoot = $"{windowsDriveLetter}:\\";
        string windowsDirectory = Path.Combine(windowsRoot, "Windows");

        List<string> transcript = new();
        List<string> warnings = new();

        void report(string message, int? percentage = null) =>
            progress?.Report(new WimDeploymentProgress(percentage, message));

        cancellationToken.ThrowIfCancellationRequested();

        report("Preparing deployment...");
        ProcessExecutionResult power = await TrySetHighPerformancePowerSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (!power.Success)
        {
            warnings.Add("The high-performance power scheme could not be selected. Deployment continued using the current power scheme.");
            AppendTranscript(transcript, "Power scheme", power);
        }

        cancellationToken.ThrowIfCancellationRequested();
        report(firmwareType == WimDeploymentFirmwareType.Uefi
            ? "Preparing disk for UEFI/GPT deployment..."
            : "Preparing disk for BIOS/MBR deployment...");

        cancellationToken.ThrowIfCancellationRequested();
        destructiveOperationStarting?.Invoke();
        ProcessExecutionResult partitionResult = await DiskPartRunner.RunAsync(
            BuildCreatePartitionsScript(disk.DiskNumber, firmwareType, windowsDriveLetter),
            cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "Create partitions", partitionResult);
        if (!partitionResult.Success)
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                "DiskPart could not create the deployment partition layout.");
        }

        if (!Directory.Exists(windowsRoot) || !Directory.Exists(@"S:\") || !Directory.Exists(@"R:\"))
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                $"The deployment partitions were created, but the expected {windowsDriveLetter}:, S:, and R: access paths are not all available.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report($"Applying {image.DisplayName}...", 0);
        Progress<WimOperationProgress> dismProgress = new(update =>
            progress?.Report(new WimDeploymentProgress(update.Percentage, update.Message)));

        WimOperationResult applyResult = await _wimBackend.ApplyAsync(
            windowsRoot,
            imagePath,
            image.Index,
            dismProgress,
            cancellationToken).ConfigureAwait(false);
        transcript.Add("=== Apply WIM ===");
        if (!string.IsNullOrWhiteSpace(applyResult.Output))
            transcript.Add(applyResult.Output);

        if (applyResult.Canceled || cancellationToken.IsCancellationRequested)
        {
            return new WimDeploymentResult
            {
                Success = false,
                Canceled = true,
                FirmwareType = firmwareType,
                Output = string.Join(Environment.NewLine + Environment.NewLine, transcript),
                Warnings = warnings.ToArray()
            };
        }

        if (!applyResult.Success)
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                $"DISM failed while applying WIM image index {image.Index}.");
        }

        if (!Directory.Exists(windowsDirectory))
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                $"The selected WIM image applied successfully, but it does not contain a Windows directory at {windowsDirectory}. Deploy WIM requires a Windows installation image so boot files can be configured.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report("Configuring boot files...");
        ProcessExecutionResult bcdBoot = await RunBcdBootAsync(windowsDirectory, cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "BCDBoot", bcdBoot);
        if (!bcdBoot.Success)
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                "The Windows image was applied, but BCDBoot could not configure the system partition.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report("Configuring Windows Recovery Environment...");
        await ConfigureRecoveryAsync(windowsDirectory, transcript, warnings, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        report("Hiding the Recovery partition...");
        ProcessExecutionResult hideRecovery = await DiskPartRunner.RunAsync(
            BuildHideRecoveryScript(disk.DiskNumber, firmwareType),
            cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "Hide Recovery partition", hideRecovery);
        if (!hideRecovery.Success)
        {
            warnings.Add("Windows was deployed, but the Recovery partition could not be fully hidden. It may remain visible until corrected.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report("Verifying Windows RE configuration...");
        ProcessExecutionResult verifyRe = await RunReagentcInfoAsync(windowsDirectory, cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "REAgentC info", verifyRe);
        int recoveryPartitionNumber = firmwareType == WimDeploymentFirmwareType.Uefi ? 4 : 3;
        if (!TryValidateWindowsReLocation(verifyRe, disk.DiskNumber, recoveryPartitionNumber, out string winReValidationError))
            warnings.Add(winReValidationError);

        report("Deployment complete.", 100);
        return new WimDeploymentResult
        {
            Success = true,
            Canceled = false,
            FirmwareType = firmwareType,
            Output = string.Join(Environment.NewLine + Environment.NewLine, transcript),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static async Task ConfigureRecoveryAsync(
        string windowsDirectory,
        List<string> transcript,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string sourceWinRe = Path.Combine(windowsDirectory, "System32", "Recovery", "winre.wim");
        string recoveryDirectory = @"R:\Recovery\WindowsRE";
        string targetWinRe = Path.Combine(recoveryDirectory, "winre.wim");

        if (!File.Exists(sourceWinRe))
        {
            warnings.Add(
                "The applied WIM does not contain Windows\\System32\\Recovery\\winre.wim. " +
                "Windows was deployed, but the Recovery partition could not be populated automatically.");
            transcript.Add("=== Windows RE ===\nwinre.wim was not present in the applied Windows image.");
            return;
        }

        try
        {
            Directory.CreateDirectory(recoveryDirectory);
            await CopyFileAsync(sourceWinRe, targetWinRe, cancellationToken).ConfigureAwait(false);
            try
            {
                File.SetAttributes(targetWinRe, File.GetAttributes(sourceWinRe));
            }
            catch
            {
            }

            transcript.Add($"=== Windows RE copy ===\nCopied {sourceWinRe} to {targetWinRe}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add("winre.wim could not be copied to the Recovery partition.");
            transcript.Add($"=== Windows RE copy ===\n{ex.Message}");
            return;
        }

        try
        {
            ProcessExecutionResult setRe = await ProcessExecutionRunner.RunAsync(
                ResolveAppliedOrSystemTool(windowsDirectory, "reagentc.exe"),
                new[]
                {
                    "/Setreimage",
                    "/Path",
                    recoveryDirectory,
                    "/Target",
                    windowsDirectory
                },
                cancellationToken).ConfigureAwait(false);
            AppendTranscript(transcript, "REAgentC setreimage", setRe);
            if (!setRe.Success)
                warnings.Add("winre.wim was copied to the Recovery partition, but REAgentC could not register it with the applied Windows installation.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add("winre.wim was copied to the Recovery partition, but REAgentC could not be run to register it with the applied Windows installation.");
            transcript.Add($"=== REAgentC setreimage ===\n{ex.Message}");
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 1024;
        await using FileStream sourceStream = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destinationStream = new(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            await sourceStream.CopyToAsync(destinationStream, BufferSize, cancellationToken).ConfigureAwait(false);
            await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { destinationStream.Close(); } catch { }
            try { File.Delete(destination); } catch { }
            throw;
        }
    }

    private static string BuildCreatePartitionsScript(
        int diskNumber,
        WimDeploymentFirmwareType firmwareType,
        char windowsDriveLetter)
    {
        if (firmwareType == WimDeploymentFirmwareType.Uefi)
        {
            return
                $"select disk {diskNumber}\r\n" +
                "clean\r\n" +
                "convert gpt\r\n" +
                "create partition efi size=" + SystemPartitionSizeMb + "\r\n" +
                "format quick fs=fat32 label=\"System\"\r\n" +
                "assign letter=S\r\n" +
                "create partition msr size=16\r\n" +
                "create partition primary\r\n" +
                "shrink minimum=" + RecoveryPartitionSizeMb + "\r\n" +
                "format quick fs=ntfs label=\"Windows\"\r\n" +
                $"assign letter={windowsDriveLetter}\r\n" +
                "create partition primary\r\n" +
                "format quick fs=ntfs label=\"Recovery\"\r\n" +
                "assign letter=R\r\n" +
                "set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"\r\n" +
                "gpt attributes=0x8000000000000001\r\n" +
                "exit\r\n";
        }

        return
            $"select disk {diskNumber}\r\n" +
            "clean\r\n" +
            "create partition primary size=" + SystemPartitionSizeMb + "\r\n" +
            "format quick fs=ntfs label=\"System\"\r\n" +
            "assign letter=S\r\n" +
            "active\r\n" +
            "create partition primary\r\n" +
            "shrink minimum=" + RecoveryPartitionSizeMb + "\r\n" +
            "format quick fs=ntfs label=\"Windows\"\r\n" +
            $"assign letter={windowsDriveLetter}\r\n" +
            "create partition primary\r\n" +
            "format quick fs=ntfs label=\"Recovery image\"\r\n" +
            "assign letter=R\r\n" +
            "set id=27\r\n" +
            "exit\r\n";
    }

    private static string BuildHideRecoveryScript(int diskNumber, WimDeploymentFirmwareType firmwareType)
    {
        if (firmwareType == WimDeploymentFirmwareType.Uefi)
        {
            return
                $"select disk {diskNumber}\r\n" +
                "select partition 4\r\n" +
                "remove letter=R\r\n" +
                "set id=de94bba4-06d1-4d40-a16a-bfd50179d6ac\r\n" +
                "gpt attributes=0x8000000000000001\r\n" +
                "exit\r\n";
        }

        return
            $"select disk {diskNumber}\r\n" +
            "select partition 3\r\n" +
            "set id=27\r\n" +
            "remove letter=R\r\n" +
            "exit\r\n";
    }

    public async Task<string?> CleanupTemporaryDeploymentDriveLettersAsync(
        int diskNumber,
        WimDeploymentFirmwareType firmwareType,
        CancellationToken cancellationToken)
    {
        int recoveryPartitionNumber = firmwareType == WimDeploymentFirmwareType.Uefi ? 4 : 3;
        (char Letter, int PartitionNumber)[] assignments =
        {
            ('S', 1),
            ('R', recoveryPartitionNumber)
        };

        List<string> errors = new();
        foreach (var (letter, partitionNumber) in assignments)
        {
            if (!IsDriveLetterPresent(letter))
                continue;

            ProcessExecutionResult result;
            try
            {
                result = await DiskPartRunner.RunAsync(
                    $"select disk {diskNumber}\r\n" +
                    $"select partition {partitionNumber}\r\n" +
                    $"remove letter={letter} noerr\r\n" +
                    "exit\r\n",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add($"{letter}: could not be removed from Disk {diskNumber}, Partition {partitionNumber}: {ex.Message}");
                continue;
            }

            for (int attempt = 0; attempt < 10 && IsDriveLetterPresent(letter); attempt++)
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);

            if (!result.Success || IsDriveLetterPresent(letter))
            {
                string detail = string.IsNullOrWhiteSpace(result.CombinedOutput)
                    ? $"DiskPart exited with code {result.ExitCode}."
                    : result.CombinedOutput;
                errors.Add($"{letter}: could not be removed from Disk {diskNumber}, Partition {partitionNumber}. {detail}");
            }
        }

        return errors.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, errors);
    }

    private static bool IsDriveLetterPresent(char letter)
    {
        char normalized = char.ToUpperInvariant(letter);
        return Directory.GetLogicalDrives().Any(root =>
            root.Length >= 2 &&
            char.ToUpperInvariant(root[0]) == normalized &&
            root[1] == ':');
    }

    private static bool TryValidateWindowsReLocation(
        ProcessExecutionResult result,
        int diskNumber,
        int recoveryPartitionNumber,
        out string error)
    {
        if (!result.Success)
        {
            error = "Windows RE configuration could not be verified after deployment.";
            return false;
        }

        string expectedDevice = $"harddisk{diskNumber}\\partition{recoveryPartitionNumber}\\Recovery\\WindowsRE";
        string? locationLine = result.CombinedOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => line.StartsWith("Windows RE location", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(locationLine))
        {
            error = "REAgentC completed, but it did not report a Windows RE location for the deployed Windows installation.";
            return false;
        }

        int colon = locationLine.IndexOf(':');
        string location = colon >= 0 ? locationLine[(colon + 1)..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(location) ||
            !location.Contains(expectedDevice, StringComparison.OrdinalIgnoreCase))
        {
            error =
                $"Windows RE is registered, but REAgentC did not report the expected recovery location on " +
                $"Disk {diskNumber}, Partition {recoveryPartitionNumber}. Reported location: " +
                (string.IsNullOrWhiteSpace(location) ? "(none)" : location);
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static async Task<ProcessExecutionResult> TrySetHighPerformancePowerSchemeAsync(CancellationToken cancellationToken)
    {
        string powerCfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        if (!File.Exists(powerCfg))
            return ProcessExecutionResult.Failed("powercfg.exe was not found.");

        return await ProcessExecutionRunner.RunAsync(powerCfg, new[] { "/s", HighPerformanceScheme }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessExecutionResult> RunBcdBootAsync(
        string windowsDirectory,
        CancellationToken cancellationToken)
    {
        string bcdBoot = ResolveAppliedOrSystemTool(windowsDirectory, "bcdboot.exe");
        return await ProcessExecutionRunner.RunAsync(
            bcdBoot,
            new[] { windowsDirectory, "/s", "S:", "/f", "ALL" },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessExecutionResult> RunReagentcInfoAsync(
        string windowsDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            string reagentc = ResolveAppliedOrSystemTool(windowsDirectory, "reagentc.exe");
            return await ProcessExecutionRunner.RunAsync(
                reagentc,
                new[] { "/Info", "/Target", windowsDirectory },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProcessExecutionResult.Failed(ex.Message);
        }
    }

    private static string ResolveAppliedOrSystemTool(string windowsDirectory, string fileName)
    {
        string applied = Path.Combine(windowsDirectory, "System32", fileName);
        if (File.Exists(applied))
            return applied;

        string system = Path.Combine(Environment.SystemDirectory, fileName);
        if (File.Exists(system))
            return system;

        throw new FileNotFoundException($"{fileName} was not found in the applied Windows image or the active Windows system directory.", fileName);
    }

    private static WimDeploymentResult Failed(
        WimDeploymentFirmwareType firmwareType,
        List<string> transcript,
        List<string> warnings,
        string message)
    {
        transcript.Add(message);
        return new WimDeploymentResult
        {
            Success = false,
            Canceled = false,
            FirmwareType = firmwareType,
            Output = string.Join(Environment.NewLine + Environment.NewLine, transcript),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static void AppendTranscript(List<string> transcript, string heading, ProcessExecutionResult result)
    {
        StringBuilder text = new();
        text.AppendLine($"=== {heading} ===");
        text.AppendLine($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            text.AppendLine(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            text.AppendLine(result.StandardError);
        transcript.Add(text.ToString().TrimEnd());
    }

    private enum FirmwareType : uint
    {
        Unknown = 0,
        Bios = 1,
        Uefi = 2,
        Max = 3
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out FirmwareType firmwareType);
}
