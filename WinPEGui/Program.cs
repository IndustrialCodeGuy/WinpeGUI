using Shell.Core.Host;
using System.Diagnostics;
using System.Text.Json;

namespace WinPEGui;

internal sealed record LauncherConfig(LauncherSettings Launcher);
internal sealed record LauncherSettings(
    ShellSettings Shell,
    FileManagerSettings? FileManager,
    int RestartDelayMs,
    int CrashBurstLimit,
    int CrashBurstWindowSeconds,
    LogSettings Log);

internal sealed record ShellSettings(string Path, string? Args);
internal sealed class FileManagerSettings
{
    public string Path { get; init; } = "FileManager.exe";
    public string? Args { get; init; } = "-host";
    public bool Restart { get; init; } = true;
}
internal sealed record LogSettings(string Target, string FileName);

internal static class Program
{
    // WinPE shell launcher/supervisor. It starts Shell.Taskbar.Host and FileManager
    // as separate processes so the taskbar UI stays off the file-manager UI thread.
    // The launcher restarts either process after exits/crashes. Power actions are
    // owned by Shell.Taskbar.Host now; launcher-triggered power is only retained
    // for guarded fatal-startup/crash-storm cases.

    private const int ExitShutdown = 0;
    private const int ExitReboot = 2;
    private const int PowerCommandWaitMs = 15_000;
    private const int SupervisorPollMs = 250;

    private const int DefaultRestartDelayMs = 500;
    private const int MinRestartDelayMs = 50;
    private const int MaxRestartDelayMs = 60_000;

    private const int DefaultCrashBurstLimit = 8;
    private const int MinCrashBurstLimit = 1;
    private const int MaxCrashBurstLimit = 1_000;

    private const int DefaultCrashBurstWindowSeconds = 30;
    private const int MinCrashBurstWindowSeconds = 1;
    private const int MaxCrashBurstWindowSeconds = 86_400;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    [STAThread]
    private static int Main(string[] args)
    {
        string launcherDir = AppContext.BaseDirectory;
        try { Environment.CurrentDirectory = launcherDir; } catch { }

        string settingsPath = Path.Combine(launcherDir, "WinPEGui.settings.json");
        bool createdDefaults = EnsureDefaultSettingsFileExists(launcherDir);

        LauncherSettings? settings = LoadSettingsOrNull(launcherDir, out string? settingsLoadError);

        string? shellOverride = null;
        string? argsOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--shell", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                shellOverride = args[++i];
                continue;
            }

            if (args[i].Equals("--args", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                argsOverride = args[++i];
                continue;
            }
        }

        string shellArgs = argsOverride ?? settings?.Shell?.Args ?? string.Empty;

        if (shellOverride != null && LooksLikeOptionToken(shellOverride))
        {
            // Treat "--args" or another option token accidentally consumed as invalid.
            shellOverride = null;
        }

        string? shellSettingPath = shellOverride ?? settings?.Shell?.Path;

        // Resolve log path early so even configuration errors can be logged.
        string logTarget = settings?.Log?.Target ?? launcherDir;
        string logFileName = settings?.Log?.FileName ?? "WinPEGui.log";
        string logRoot = ResolveLogRoot(logTarget, launcherDir);
        string logPath = Path.Combine(logRoot, logFileName);

        if (createdDefaults)
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Created default settings file: \"{settingsPath}\"{Environment.NewLine}");
        }

        SafeAppend(logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Launcher start. launcherDir=\"{launcherDir}\" logPath=\"{logPath}\"{Environment.NewLine}");

        if (!string.IsNullOrWhiteSpace(settingsLoadError))
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Settings load failed. {settingsLoadError} " +
                $"Using built-in defaults where possible.{Environment.NewLine}");
        }

        if (string.IsNullOrWhiteSpace(shellSettingPath))
        {
            string reason;

            if (shellOverride != null)
                reason = "--shell was provided but empty/blank or invalid.";
            else if (!File.Exists(settingsPath))
                reason = "No settings file and no --shell provided.";
            else if (settings == null)
                reason = "Settings file present but failed to load/parse, and no --shell provided.";
            else
                reason = "Settings loaded but Shell.Path is missing/blank, and no --shell provided.";

            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Missing shell path. {reason} Requesting shutdown.{Environment.NewLine}");

            return RequestPowerActionOrHold(reboot: false, logPath);
        }

        int restartDelayMs = settings is null
            ? DefaultRestartDelayMs
            : NormalizeIntSetting(
                nameof(LauncherSettings.RestartDelayMs),
                settings.RestartDelayMs,
                DefaultRestartDelayMs,
                MinRestartDelayMs,
                MaxRestartDelayMs,
                logPath);

        int crashBurstLimit = settings is null
            ? DefaultCrashBurstLimit
            : NormalizeIntSetting(
                nameof(LauncherSettings.CrashBurstLimit),
                settings.CrashBurstLimit,
                DefaultCrashBurstLimit,
                MinCrashBurstLimit,
                MaxCrashBurstLimit,
                logPath);

        int crashBurstWindowSeconds = settings is null
            ? DefaultCrashBurstWindowSeconds
            : NormalizeIntSetting(
                nameof(LauncherSettings.CrashBurstWindowSeconds),
                settings.CrashBurstWindowSeconds,
                DefaultCrashBurstWindowSeconds,
                MinCrashBurstWindowSeconds,
                MaxCrashBurstWindowSeconds,
                logPath);

        TimeSpan crashBurstWindow = TimeSpan.FromSeconds(crashBurstWindowSeconds);

        string shellPath = ResolveShellPath(launcherDir, shellSettingPath);

        if (!File.Exists(shellPath))
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Shell EXE not found: \"{shellPath}\". Requesting shutdown.{Environment.NewLine}");

            return RequestPowerActionOrHold(reboot: false, logPath);
        }

        SupervisedProcess shell = new(
            role: "Shell",
            path: shellPath,
            args: shellArgs,
            restart: true,
            powerExitCodes: false,
            probeFileManager: false);

        SupervisedProcess? fileManager = CreateFileManagerProcess(launcherDir, settings);
        if (fileManager is not null && !File.Exists(fileManager.Path))
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] File Manager EXE not found: \"{fileManager.Path}\". Requesting shutdown.{Environment.NewLine}");

            return RequestPowerActionOrHold(reboot: false, logPath);
        }

        IReadOnlyList<string> driveLetterPolicyMessages = WinPeDriveLetterPolicy.NormalizePrimaryWindowsDrive(new[]
        {
            launcherDir,
            shellPath,
            fileManager?.Path
        });

        // The startup drive-letter policy can move the volume that owns the configured
        // log label/drive. Resolve it again before the supervised shell processes start.
        logRoot = ResolveLogRoot(logTarget, launcherDir);
        logPath = Path.Combine(logRoot, logFileName);
        foreach (string message in driveLetterPolicyMessages)
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Drive-letter policy: {message}{Environment.NewLine}");
        }

        var launcherSw = Stopwatch.StartNew();

        if (fileManager is not null)
            StartSupervisedProcess(fileManager, launcherDir, logPath, launcherSw.ElapsedMilliseconds);

        StartSupervisedProcess(shell, launcherDir, logPath, launcherSw.ElapsedMilliseconds);

        while (true)
        {
            long systemUpMs = launcherSw.ElapsedMilliseconds;

            if (fileManager is not null)
            {
                int? exitCode = TryConsumeExitedProcess(fileManager, out long runMs);
                if (exitCode.HasValue)
                {
                    if (exitCode.Value == 0 && fileManager.ProbeFileManager && TryProbeFileManager())
                    {
                        fileManager.MarkExternalSatisfied();
                        LogExternalProcessSatisfied(logPath, fileManager, systemUpMs);
                        continue;
                    }

                    bool crashStorm = RegisterProcessFailure(
                        fileManager,
                        crashBurstLimit,
                        crashBurstWindow,
                        systemUpMs);

                    Exception? logEx = crashStorm
                        ? new InvalidOperationException(
                            $"Crash storm: {fileManager.BurstCrashes} {fileManager.Role} exits within {crashBurstWindow.TotalSeconds:0}s -> shutdown.")
                        : fileManager.ConsumeLastFailureException();

                    LogProcessExit(logPath, fileManager, exitCode.Value, logEx, runMs, systemUpMs);

                    if (crashStorm)
                        return RequestPowerActionOrHold(reboot: false, logPath);

                    if (fileManager.Restart)
                    {
                        if (restartDelayMs > 0)
                            Thread.Sleep(restartDelayMs);

                        StartSupervisedProcess(fileManager, launcherDir, logPath, launcherSw.ElapsedMilliseconds);
                    }
                    else
                    {
                        fileManager.DisableMonitoring();
                    }
                }
            }

            int? shellExitCode = TryConsumeExitedProcess(shell, out long shellRunMs);
            if (shellExitCode.HasValue)
            {
                if (shell.UsesPowerExitCodes && (shellExitCode.Value == ExitShutdown || shellExitCode.Value == ExitReboot))
                {
                    bool reboot = shellExitCode.Value == ExitReboot;

                    SafeAppend(logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Shell clean exit -> {(reboot ? "reboot" : "shutdown")}. " +
                        $"exitCode={shellExitCode.Value}, runMs={shellRunMs}, systemUp={FormatMs(systemUpMs)} ({systemUpMs}ms){Environment.NewLine}");

                    return RequestPowerActionOrHold(reboot, logPath);
                }

                bool crashStorm = RegisterProcessFailure(
                    shell,
                    crashBurstLimit,
                    crashBurstWindow,
                    systemUpMs);

                Exception? logEx = crashStorm
                    ? new InvalidOperationException(
                        $"Crash storm: {shell.BurstCrashes} {shell.Role} exits within {crashBurstWindow.TotalSeconds:0}s -> shutdown.")
                    : shell.ConsumeLastFailureException();

                LogProcessExit(logPath, shell, shellExitCode.Value, logEx, shellRunMs, systemUpMs);

                if (crashStorm)
                    return RequestPowerActionOrHold(reboot: false, logPath);

                if (shell.Restart)
                {
                    if (restartDelayMs > 0)
                        Thread.Sleep(restartDelayMs);

                    StartSupervisedProcess(shell, launcherDir, logPath, launcherSw.ElapsedMilliseconds);
                }
                else
                {
                    shell.DisableMonitoring();
                }
            }

            Thread.Sleep(SupervisorPollMs);
        }
    }

    private static SupervisedProcess? CreateFileManagerProcess(string launcherDir, LauncherSettings? settings)
    {
        FileManagerSettings? hostSettings = settings?.FileManager;

        string hostPathSetting = hostSettings?.Path ?? "FileManager.exe";
        string hostArgs = hostSettings?.Args ?? "-host";
        bool restart = hostSettings?.Restart ?? true;

        if (string.IsNullOrWhiteSpace(hostPathSetting))
            return null;

        return new SupervisedProcess(
            role: "File Manager",
            path: ResolveShellPath(launcherDir, hostPathSetting),
            args: hostArgs,
            restart: restart,
            powerExitCodes: false,
            probeFileManager: true);
    }

    private static void StartSupervisedProcess(
        SupervisedProcess item,
        string launcherDir,
        string logPath,
        long systemUpMs)
    {
        item.DisposeProcess();
        item.LastStartUtc = DateTime.UtcNow;

        if (item.ProbeFileManager && TryProbeFileManager())
        {
            item.MarkExternalSatisfied();
            LogExternalProcessSatisfied(logPath, item, systemUpMs);
            return;
        }

        item.ClearExternalSatisfied();

        try
        {
            string processArgs = UsesSessionOwnerProcessId(item.Role)
                ? AppendSessionOwnerProcessId(item.Args)
                : item.Args;

            var psi = new ProcessStartInfo
            {
                FileName = item.Path,
                Arguments = processArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(item.Path) ?? launcherDir,
            };

            Process? process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Process.Start returned null.");

            item.Process = process;
            item.ClearLastFailureException();

            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Started {item.Role}. " +
                $"pid={process.Id}, path=\"{item.Path}\", args=\"{processArgs}\", systemUp={FormatMs(systemUpMs)} ({systemUpMs}ms){Environment.NewLine}");
        }
        catch (Exception ex)
        {
            item.Process = null;
            item.LastStartUtc = DateTime.UtcNow;
            item.SetLastFailureException(ex);
        }
    }


    private static bool UsesSessionOwnerProcessId(string role)
    {
        return role.Equals("Shell", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("File Manager", StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendSessionOwnerProcessId(string args)
    {
        string ownerArg = $"--session-owner-pid {Environment.ProcessId}";

        if (string.IsNullOrWhiteSpace(args))
            return ownerArg;

        return $"{args} {ownerArg}";
    }

    private static int? TryConsumeExitedProcess(SupervisedProcess item, out long runMs)
    {
        runMs = 0;

        if (item.MonitoringDisabled)
            return null;

        if (item.ExternalSatisfied)
        {
            if (item.ProbeFileManager && TryProbeFileManager())
                return null;

            item.ClearExternalSatisfied();
            runMs = item.GetCurrentRunMs();
            return 1;
        }

        Process? process = item.Process;

        if (process == null)
        {
            runMs = item.GetCurrentRunMs();
            return 1;
        }

        try
        {
            if (!process.HasExited)
                return null;

            int exitCode = process.ExitCode;
            runMs = item.GetCurrentRunMs();
            item.DisposeProcess();
            return exitCode;
        }
        catch (Exception ex)
        {
            runMs = item.GetCurrentRunMs();
            item.SetLastFailureException(ex);
            item.DisposeProcess();
            return 1;
        }
    }

    private static bool RegisterProcessFailure(
        SupervisedProcess item,
        int crashBurstLimit,
        TimeSpan crashBurstWindow,
        long systemUpMs)
    {
        item.RestartCount++;

        long crashBurstWindowMs = (long)crashBurstWindow.TotalMilliseconds;

        if (item.BurstWindowStartMs < 0 || systemUpMs - item.BurstWindowStartMs > crashBurstWindowMs)
        {
            item.BurstWindowStartMs = systemUpMs;
            item.BurstCrashes = 1;
            return false;
        }

        item.BurstCrashes++;
        return item.BurstCrashes >= crashBurstLimit;
    }

    private static bool TryProbeFileManager()
    {
        return ExplorerHostClient.TrySignalOpenWindow(
            new ExplorerLaunchRequest { HostOnly = true },
            timeoutMs: 500);
    }

    private static void LogExternalProcessSatisfied(
        string logPath,
        SupervisedProcess item,
        long systemUpMs)
    {
        SafeAppend(logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {item.Role} is already running and responding over IPC. " +
            $"path=\"{item.Path}\", args=\"{item.Args}\", systemUp={FormatMs(systemUpMs)} ({systemUpMs}ms){Environment.NewLine}");
    }

    private static void LogProcessExit(
        string logPath,
        SupervisedProcess item,
        int exitCode,
        Exception? ex,
        long runMs,
        long systemUpMs)
    {
        string action = item.Restart ? "restarting" : "not restarting";
        string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string msg =
            $"[{ts}] {item.Role} non-clean exit -> {action}. " +
            $"exitCode={exitCode}, restartCount={item.RestartCount}, " +
            $"runMs={runMs}, systemUp={FormatMs(systemUpMs)} ({systemUpMs}ms), " +
            $"path=\"{item.Path}\", args=\"{item.Args}\"";

        if (ex != null)
            msg += $", ex={ex.GetType().Name}: {ex.Message}";

        msg += Environment.NewLine;

        SafeAppend(logPath, msg);
    }

    private sealed class SupervisedProcess
    {
        public SupervisedProcess(
            string role,
            string path,
            string args,
            bool restart,
            bool powerExitCodes,
            bool probeFileManager)
        {
            Role = role;
            Path = path;
            Args = args;
            Restart = restart;
            UsesPowerExitCodes = powerExitCodes;
            ProbeFileManager = probeFileManager;
        }

        public string Role { get; }
        public string Path { get; }
        public string Args { get; }
        public bool Restart { get; }
        public bool UsesPowerExitCodes { get; }
        public bool ProbeFileManager { get; }
        public Process? Process { get; set; }
        public DateTime LastStartUtc { get; set; } = DateTime.UtcNow;
        public int RestartCount { get; set; }
        public int BurstCrashes { get; set; }
        public long BurstWindowStartMs { get; set; } = -1;
        public bool MonitoringDisabled { get; private set; }
        public bool ExternalSatisfied { get; private set; }
        private Exception? LastFailureException { get; set; }

        public long GetCurrentRunMs()
        {
            TimeSpan elapsed = DateTime.UtcNow - LastStartUtc;
            return elapsed.TotalMilliseconds <= 0 ? 0 : (long)elapsed.TotalMilliseconds;
        }

        public void SetLastFailureException(Exception ex)
        {
            LastFailureException = ex;
        }

        public Exception? ConsumeLastFailureException()
        {
            Exception? ex = LastFailureException;
            LastFailureException = null;
            return ex;
        }

        public void ClearLastFailureException()
        {
            LastFailureException = null;
        }

        public void DisableMonitoring()
        {
            MonitoringDisabled = true;
        }

        public void MarkExternalSatisfied()
        {
            ExternalSatisfied = true;
        }

        public void ClearExternalSatisfied()
        {
            ExternalSatisfied = false;
        }

        public void DisposeProcess()
        {
            Process? process = Process;
            Process = null;

            if (process == null)
                return;

            try { process.Dispose(); }
            catch { }
        }
    }

    private static int RequestPowerActionOrHold(bool reboot, string logPath)
    {
        if (!IsRunningInWinPE())
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Launcher requested {(reboot ? "reboot" : "shutdown")}, " +
                $"but this is not WinPE. Holding instead of executing a power command.{Environment.NewLine}");

            // In Full Windows/VS, never let launcher startup/configuration problems
            // turn into a real machine shutdown. Keep the process alive so the log
            // can be inspected and Visual Studio does not immediately relaunch it.
            Thread.Sleep(Timeout.Infinite);
        }

        if (!TryRequestPowerAction(reboot, logPath, out string? error))
        {
            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Unable to request {(reboot ? "reboot" : "shutdown")}. {error}{Environment.NewLine}");

            // Keep the launcher alive rather than letting winpeshl.exe immediately reboot WinPE.
            Thread.Sleep(Timeout.Infinite);
        }

        SafeAppend(logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Power command accepted; holding launcher process while system {(reboot ? "reboots" : "shuts down")}.{Environment.NewLine}");

        Thread.Sleep(Timeout.Infinite);
        return reboot ? ExitReboot : ExitShutdown;
    }

    private static bool IsRunningInWinPE()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? miniNt =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");

            if (miniNt != null)
                return true;
        }
        catch { }

        try
        {
            using Microsoft.Win32.RegistryKey? winPe =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinPE");

            if (winPe != null)
                return true;
        }
        catch { }

        try
        {
            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty;
            return systemRoot.StartsWith(@"X:\Windows", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRequestPowerAction(bool reboot, string logPath, out string? error)
    {
        try
        {
            ProcessStartInfo psi = BuildSystemPowerStartInfo(reboot);

            SafeAppend(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Starting power command. file=\"{psi.FileName}\" args=\"{psi.Arguments}\"{Environment.NewLine}");

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                error = "Process.Start returned null.";
                return false;
            }

            if (process.WaitForExit(PowerCommandWaitMs) && process.ExitCode != 0)
            {
                error = $"Power command exited with code {process.ExitCode}.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static ProcessStartInfo BuildSystemPowerStartInfo(bool reboot)
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

        if (string.IsNullOrWhiteSpace(systemDirectory))
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDirectory))
                systemDirectory = Path.Combine(windowsDirectory, "System32");
        }

        if (string.IsNullOrWhiteSpace(systemDirectory))
            systemDirectory = @"X:\Windows\System32";

        string wpeutilPath = Path.Combine(systemDirectory, "wpeutil.exe");
        if (File.Exists(wpeutilPath))
        {
            return new ProcessStartInfo
            {
                FileName = wpeutilPath,
                Arguments = reboot ? "reboot" : "shutdown",
                WorkingDirectory = systemDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = Path.Combine(systemDirectory, "shutdown.exe"),
            Arguments = reboot ? "/r /t 0" : "/s /t 0",
            WorkingDirectory = systemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static void SafeAppend(string logPath, string text)
    {
        try
        {
            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.AppendAllText(logPath, text);
        }
        catch { }
    }

    private static string ResolveShellPath(string launcherDir, string shellPathSetting)
    {
        string expanded = Environment.ExpandEnvironmentVariables(shellPathSetting.Trim());

        if (Path.IsPathRooted(expanded))
            return expanded;

        return Path.Combine(launcherDir, expanded);
    }

    // Target supports:
    //   "label:WinPE"   -> match volume label
    //   "drive:E"       -> drive letter
    //   "X:\Logs"      -> absolute directory path
    // No prefix => treat as LABEL first (so "E:" as a label works), then fall back.
    private static string ResolveLogRoot(string target, string fallbackDir)
    {
        string t = (target ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(t) && Path.IsPathRooted(t))
        {
            string? root = Path.GetPathRoot(t);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return t;
        }

        if (t.StartsWith("label:", StringComparison.OrdinalIgnoreCase))
        {
            string label = t.Substring("label:".Length).Trim();
            string byLabel = ResolveLabelRoot(label);
            if (!string.IsNullOrEmpty(byLabel)) return byLabel;

            string byRemovable = ResolveFirstRemovableFat32OrExFat();
            if (!string.IsNullOrEmpty(byRemovable)) return byRemovable;

            return fallbackDir;
        }

        if (t.StartsWith("drive:", StringComparison.OrdinalIgnoreCase))
        {
            string drive = t.Substring("drive:".Length).Trim();
            string byDrive = ResolveDriveRoot(drive);
            if (!string.IsNullOrEmpty(byDrive)) return byDrive;

            string byRemovable = ResolveFirstRemovableFat32OrExFat();
            if (!string.IsNullOrEmpty(byRemovable)) return byRemovable;

            return fallbackDir;
        }

        string byLabelNoPrefix = ResolveLabelRoot(t);
        if (!string.IsNullOrEmpty(byLabelNoPrefix)) return byLabelNoPrefix;

        string byDriveNoPrefix = ResolveDriveRoot(t);
        if (!string.IsNullOrEmpty(byDriveNoPrefix)) return byDriveNoPrefix;

        string byFirstRemovable = ResolveFirstRemovableFat32OrExFat();
        if (!string.IsNullOrEmpty(byFirstRemovable)) return byFirstRemovable;

        return fallbackDir;
    }

    private static string ResolveDriveRoot(string driveToken)
    {
        try
        {
            string d = (driveToken ?? string.Empty).Trim();

            if (d.Length == 1 && char.IsLetter(d[0]))
            {
                char letter = char.ToUpperInvariant(d[0]);
                string root = $"{letter}:\\";
                return Directory.Exists(root) ? root : string.Empty;
            }

            if (d.Length == 2 && char.IsLetter(d[0]) && d[1] == ':')
            {
                char letter = char.ToUpperInvariant(d[0]);
                string root = $"{letter}:\\";
                return Directory.Exists(root) ? root : string.Empty;
            }
        }
        catch { }

        return string.Empty;
    }

    private static string ResolveLabelRoot(string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                if (!di.IsReady) continue;
                if (string.Equals(di.VolumeLabel, label, StringComparison.OrdinalIgnoreCase))
                    return di.RootDirectory.FullName;
            }
        }
        catch { }

        return string.Empty;
    }

    private static string ResolveFirstRemovableFat32OrExFat()
    {
        try
        {
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                if (!di.IsReady) continue;
                if (di.DriveType != DriveType.Removable) continue;

                string format = di.DriveFormat;
                if (!format.Equals("FAT32", StringComparison.OrdinalIgnoreCase) &&
                    !format.Equals("exFAT", StringComparison.OrdinalIgnoreCase))
                    continue;

                return di.RootDirectory.FullName;
            }
        }
        catch { }

        return string.Empty;
    }

    private static bool LooksLikeOptionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string v = value.TrimStart();
        return v.StartsWith("--", StringComparison.Ordinal) || v.StartsWith("/", StringComparison.Ordinal);
    }

    private static bool EnsureDefaultSettingsFileExists(string launcherDir)
    {
        try
        {
            string path = Path.Combine(launcherDir, "WinPEGui.settings.json");
            if (File.Exists(path)) return false;

            var defaults = CreateDefaultConfig();
            var writeOptions = new JsonSerializerOptions { WriteIndented = true };

            string json = JsonSerializer.Serialize(defaults, writeOptions);
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static LauncherConfig CreateDefaultConfig()
    {
        return new LauncherConfig(
            new LauncherSettings(
                Shell: new ShellSettings(
                    Path: "Shell.Taskbar.Host.exe",
                    Args: string.Empty),
                FileManager: new FileManagerSettings
                {
                    Path = "FileManager.exe",
                    Args = "-host",
                    Restart = true
                },
                RestartDelayMs: DefaultRestartDelayMs,
                CrashBurstLimit: DefaultCrashBurstLimit,
                CrashBurstWindowSeconds: DefaultCrashBurstWindowSeconds,
                Log: new LogSettings(
                    Target: "label:WinPE",
                    FileName: "WinPEGui.log")));
    }

    private static LauncherSettings? LoadSettingsOrNull(string launcherDir, out string? error)
    {
        error = null;

        try
        {
            string path = Path.Combine(launcherDir, "WinPEGui.settings.json");
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            LauncherConfig? config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);

            if (config?.Launcher is null)
            {
                error = "The settings file does not contain a valid Launcher object.";
                return null;
            }

            return config.Launcher;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    private static int NormalizeIntSetting(
        string settingName,
        int value,
        int defaultValue,
        int minValue,
        int maxValue,
        string logPath)
    {
        if (value >= minValue && value <= maxValue)
            return value;

        SafeAppend(logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Invalid {settingName}={value}. " +
            $"Using default {defaultValue}; valid range is {minValue} through {maxValue}.{Environment.NewLine}");

        return defaultValue;
    }

    private static string FormatMs(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}
