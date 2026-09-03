using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Imaging.Core;

public sealed class WimDeploymentService
{
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private readonly DismWimBackend _wimBackend;

    public WimDeploymentService(DismWimBackend wimBackend)
    {
        _wimBackend = wimBackend ?? throw new ArgumentNullException(nameof(wimBackend));
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
        string windowsDirectory,
        CancellationToken cancellationToken)
    {
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

        try
        {
            string bcdBoot = ResolveAppliedOrSystemTool(windowsFullPath, "bcdboot.exe");
            ProcessResult result = await RunProcessAsync(
                bcdBoot,
                new[] { windowsFullPath },
                cancellationToken).ConfigureAwait(false);

            return new WimBootConfigurationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                Output = result.CombinedOutput
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
    }

    public async Task<WimDeploymentResult> DeployAsync(
        ImagingDiskInfo disk,
        string imagePath,
        WimImageInfo image,
        WimDeploymentFirmwareType firmwareType,
        IProgress<WimDeploymentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(image);

        if (!File.Exists(imagePath))
            throw new FileNotFoundException("The WIM file was not found.", imagePath);
        if (firmwareType is not (WimDeploymentFirmwareType.Bios or WimDeploymentFirmwareType.Uefi))
            throw new InvalidOperationException("The current firmware mode could not be determined as BIOS or UEFI.");

        List<string> transcript = new();
        List<string> warnings = new();

        void report(string message, int? percentage = null) =>
            progress?.Report(new WimDeploymentProgress(percentage, message));

        cancellationToken.ThrowIfCancellationRequested();

        report("Preparing deployment...");
        ProcessResult power = await TrySetHighPerformancePowerSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (!power.Success)
        {
            warnings.Add("The high-performance power scheme could not be selected. Deployment continued using the current power scheme.");
            AppendTranscript(transcript, "Power scheme", power);
        }

        cancellationToken.ThrowIfCancellationRequested();
        report(firmwareType == WimDeploymentFirmwareType.Uefi
            ? "Preparing disk for UEFI/GPT deployment..."
            : "Preparing disk for BIOS/MBR deployment...");

        ProcessResult partitionResult = await RunDiskPartAsync(
            BuildCreatePartitionsScript(disk.DiskNumber, firmwareType),
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

        if (!Directory.Exists(@"C:\") || !Directory.Exists(@"S:\") || !Directory.Exists(@"R:\"))
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                "The deployment partitions were created, but the expected C:, S:, and R: access paths are not all available.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report($"Applying {image.DisplayName}...", 0);
        Progress<WimOperationProgress> dismProgress = new(update =>
            progress?.Report(new WimDeploymentProgress(update.Percentage, update.Message)));

        WimOperationResult applyResult = await _wimBackend.ApplyAsync(
            @"C:\",
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

        if (!Directory.Exists(@"C:\Windows"))
        {
            return Failed(
                firmwareType,
                transcript,
                warnings,
                "The selected WIM image applied successfully, but it does not contain a Windows directory at C:\\Windows. Deploy WIM requires a Windows installation image so boot files can be configured.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report("Configuring boot files...");
        ProcessResult bcdBoot = await RunBcdBootAsync(cancellationToken).ConfigureAwait(false);
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
        await ConfigureRecoveryAsync(transcript, warnings, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        report("Hiding the Recovery partition...");
        ProcessResult hideRecovery = await RunDiskPartAsync(
            BuildHideRecoveryScript(disk.DiskNumber, firmwareType),
            cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "Hide Recovery partition", hideRecovery);
        if (!hideRecovery.Success)
        {
            warnings.Add("Windows was deployed, but the Recovery partition could not be fully hidden. It may remain visible until corrected.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        report("Verifying Windows RE configuration...");
        ProcessResult verifyRe = await RunReagentcInfoAsync(cancellationToken).ConfigureAwait(false);
        AppendTranscript(transcript, "REAgentC info", verifyRe);
        if (!verifyRe.Success)
            warnings.Add("Windows RE configuration could not be verified after deployment.");

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
        List<string> transcript,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string sourceWinRe = @"C:\Windows\System32\Recovery\winre.wim";
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
            File.Copy(sourceWinRe, targetWinRe, overwrite: true);
            try
            {
                File.SetAttributes(targetWinRe, File.GetAttributes(sourceWinRe));
            }
            catch
            {
            }

            transcript.Add($"=== Windows RE copy ===\nCopied {sourceWinRe} to {targetWinRe}.");
        }
        catch (Exception ex)
        {
            warnings.Add("winre.wim could not be copied to the Recovery partition.");
            transcript.Add($"=== Windows RE copy ===\n{ex.Message}");
            return;
        }

        try
        {
            ProcessResult setRe = await RunProcessAsync(
                ResolveAppliedOrSystemTool("reagentc.exe"),
                new[]
                {
                    "/Setreimage",
                    "/Path",
                    recoveryDirectory,
                    "/Target",
                    @"C:\Windows"
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

    private static string BuildCreatePartitionsScript(int diskNumber, WimDeploymentFirmwareType firmwareType)
    {
        if (firmwareType == WimDeploymentFirmwareType.Uefi)
        {
            return
                $"select disk {diskNumber}\r\n" +
                "clean\r\n" +
                "convert gpt\r\n" +
                "create partition efi size=260\r\n" +
                "format quick fs=fat32 label=\"System\"\r\n" +
                "assign letter=S\r\n" +
                "create partition msr size=16\r\n" +
                "create partition primary\r\n" +
                "shrink minimum=900\r\n" +
                "format quick fs=ntfs label=\"Windows\"\r\n" +
                "assign letter=C\r\n" +
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
            "create partition primary size=100\r\n" +
            "format quick fs=ntfs label=\"System\"\r\n" +
            "assign letter=S\r\n" +
            "active\r\n" +
            "create partition primary\r\n" +
            "shrink minimum=750\r\n" +
            "format quick fs=ntfs label=\"Windows\"\r\n" +
            "assign letter=C\r\n" +
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

    private static async Task<ProcessResult> TrySetHighPerformancePowerSchemeAsync(CancellationToken cancellationToken)
    {
        string powerCfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        if (!File.Exists(powerCfg))
            return ProcessResult.Failed("powercfg.exe was not found.");

        return await RunProcessAsync(powerCfg, new[] { "/s", HighPerformanceScheme }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunBcdBootAsync(CancellationToken cancellationToken)
    {
        string bcdBoot = ResolveAppliedOrSystemTool("bcdboot.exe");
        return await RunProcessAsync(
            bcdBoot,
            new[] { @"C:\Windows", "/s", "S:", "/f", "ALL" },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunReagentcInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            string reagentc = ResolveAppliedOrSystemTool("reagentc.exe");
            return await RunProcessAsync(
                reagentc,
                new[] { "/Info", "/Target", @"C:\Windows" },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProcessResult.Failed(ex.Message);
        }
    }

    private static string ResolveAppliedOrSystemTool(string fileName) =>
        ResolveAppliedOrSystemTool(@"C:\Windows", fileName);

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

    private static async Task<ProcessResult> RunDiskPartAsync(string script, CancellationToken cancellationToken)
    {
        string diskPart = Path.Combine(Environment.SystemDirectory, "diskpart.exe");
        if (!File.Exists(diskPart))
            return ProcessResult.Failed("DiskPart.exe was not found under the active Windows system directory.");

        string scriptPath = Path.Combine(Path.GetTempPath(), $"ImagingManager-Deploy-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

        try
        {
            ProcessResult result = await RunProcessAsync(
                diskPart,
                new[] { "/s", scriptPath },
                cancellationToken).ConfigureAwait(false);

            if (result.Success && ContainsDiskPartFailure(result.CombinedOutput))
                return new ProcessResult(false, result.ExitCode, result.StandardOutput, result.StandardError);

            return result;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static bool ContainsDiskPartFailure(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        string[] markers =
        {
            "DiskPart has encountered an error",
            "Virtual Disk Service error",
            "The arguments specified for this command are not valid",
            "There is no disk selected",
            "There is no partition selected",
            "The selected disk is not valid"
        };

        return markers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = CreateProcessStartInfo(fileName, arguments);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {Path.GetFileName(fileName)}.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
            throw;
        }

        string output = (await stdoutTask.ConfigureAwait(false)).Trim();
        string error = (await stderrTask.ConfigureAwait(false)).Trim();
        return new ProcessResult(process.ExitCode == 0, process.ExitCode, output, error);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, IEnumerable<string> arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.SystemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
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

    private static void AppendTranscript(List<string> transcript, string heading, ProcessResult result)
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

    private readonly record struct ProcessResult(
        bool Success,
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }.Where(static s => !string.IsNullOrWhiteSpace(s)));

        public static ProcessResult Failed(string message) => new(false, -1, string.Empty, message);
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
