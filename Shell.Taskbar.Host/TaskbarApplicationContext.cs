using Shared.Shell.Interop;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using Shell.Core.Host;
using Shell.Core.Models;
using Shell.Infrastructure.DriveState;
using Shell.Taskbar.UI;
using System.Diagnostics;

namespace Shell.Taskbar.Host;

internal sealed class TaskbarApplicationContext : ApplicationContext
{
    private const int HostProbeTimeoutMs = 500;
    private const int HostStartSignalTimeoutMs = 5_000;

    private readonly SynchronizationContext _uiContext;
    private readonly int _sessionOwnerProcessId;
    private ShellTaskbarForm? _taskbar;
    private bool _isExiting;
    private bool _powerActionPending;

    public TaskbarApplicationContext(int sessionOwnerProcessId = 0)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("WindowsFormsSynchronizationContext was not available.");

        _sessionOwnerProcessId = sessionOwnerProcessId;

        if (sessionOwnerProcessId > 0)
            SessionOwnerMonitor.Start(sessionOwnerProcessId, SessionOwnerExited);

        BitLockerRuntimeCapabilities bitLockerCapabilities = BitLockerRuntimeCapabilities.Detect();

        bool showImagingManagerStartMenu = File.Exists(Path.Combine(AppContext.BaseDirectory, "Imaging.Manager.exe"));
        ShellTaskbarForm taskbar = new(bitLockerCapabilities.CanShowBitLockerManagerStartMenu, showImagingManagerStartMenu);
        taskbar.OpenExplorerRequested += Taskbar_OpenExplorerRequested;
        taskbar.BitLockerManagerRequested += Taskbar_BitLockerManagerRequested;
        taskbar.ImagingManagerRequested += Taskbar_ImagingManagerRequested;
        taskbar.ShutdownRequested += Taskbar_ShutdownRequested;
        taskbar.RebootRequested += Taskbar_RebootRequested;
        taskbar.FormClosed += Taskbar_FormClosed;

        _taskbar = taskbar;
        taskbar.Show();

        _ = Task.Run(EnsureFileManagerRunning);
    }

    private void SessionOwnerExited()
    {
        _uiContext.Post(_ =>
        {
            if (_isExiting)
                return;

            ExitThread();
        }, null);
    }

    private void Taskbar_OpenExplorerRequested(object? sender, EventArgs e)
    {
        // This request starts from an intentional taskbar/start-menu click.
        // The file-manager process owns the actual file-manager windows, so grant that process
        // foreground permission before the IPC hop. Without this, Windows can
        // accept the new window but only flash it instead of focusing it.
        User32.AllowSetForegroundWindow(User32.ASFW_ANY);
        _ = Task.Run(OpenFileManagerWindowThroughHost);
    }

    private void Taskbar_BitLockerManagerRequested(object? sender, EventArgs e)
    {
        User32.AllowSetForegroundWindow(User32.ASFW_ANY);
        LaunchBitLockerManager();
    }

    private void Taskbar_ImagingManagerRequested(object? sender, EventArgs e)
    {
        User32.AllowSetForegroundWindow(User32.ASFW_ANY);
        LaunchImagingManager();
    }

    private async void Taskbar_ShutdownRequested(object? sender, EventArgs e)
    {
        await RequestSystemPowerActionAsync(reboot: false);
    }

    private async void Taskbar_RebootRequested(object? sender, EventArgs e)
    {
        await RequestSystemPowerActionAsync(reboot: true);
    }

    private void Taskbar_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is ShellTaskbarForm taskbar)
            DetachTaskbar(taskbar);

        if (ReferenceEquals(_taskbar, sender))
            _taskbar = null;

        ExitThread();
    }

    private void EnsureFileManagerRunning()
    {
        if (ExplorerHostClient.TrySignalOpenWindow(new ExplorerLaunchRequest { HostOnly = true }, HostProbeTimeoutMs))
            return;

        TryStartFileManagerHidden(out _);
    }

    private void OpenFileManagerWindowThroughHost()
    {
        ExplorerLaunchRequest request = new()
        {
            Mode = ExplorerWindowMode.Browse
        };

        if (ExplorerHostClient.TrySignalOpenWindow(request, HostProbeTimeoutMs))
            return;

        if (!TryStartFileManagerHidden(out string? startError))
        {
            ShowFileManagerLaunchError(startError ?? "FileManager.exe could not be started.");
            return;
        }

        if (!ExplorerHostClient.TrySignalOpenWindow(request, HostStartSignalTimeoutMs))
        {
            ShowFileManagerLaunchError(
                "FileManager.exe was started, but the taskbar could not contact the file-manager service to open a window.");
        }
    }

    private bool TryStartFileManagerHidden(out string? error)
    {
        try
        {
            string fileManagerPath = Path.Combine(AppContext.BaseDirectory, "FileManager.exe");
            if (!File.Exists(fileManagerPath))
            {
                error = $"FileManager.exe was not found at:\n{fileManagerPath}";
                return false;
            }

            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = fileManagerPath,
                Arguments = BuildFileManagerArguments(),
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                error = "Process.Start returned null.";
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

    private string BuildFileManagerArguments()
    {
        string args = "-host" + ShellTheme.ThemeArgs;

        if (_sessionOwnerProcessId > 0)
            args += $" --session-owner-pid {_sessionOwnerProcessId}";

        return args;
    }

    private void ShowFileManagerLaunchError(string message)
    {
        _uiContext.Post(_ =>
        {
            MessageBox.Show(
                message,
                "File Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }, null);
    }

    private static void LaunchBitLockerManager()
    {
        string helperPath = Path.Combine(AppContext.BaseDirectory, "BitLocker.Manager.exe");
        if (!File.Exists(helperPath))
        {
            MessageBox.Show(
                $"BitLocker helper was not found:\n{helperPath}",
                "BitLocker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = helperPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        };

        if (ShellTheme.DarkMode)
            startInfo.ArgumentList.Add("--dark");

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to start BitLocker helper:\n{ex.Message}",
                "BitLocker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void LaunchImagingManager()
    {
        string helperPath = Path.Combine(AppContext.BaseDirectory, "Imaging.Manager.exe");
        if (!File.Exists(helperPath))
        {
            MessageBox.Show(
                $"Imaging Manager was not found:\n{helperPath}",
                "Imaging Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = helperPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true
        };

        if (ShellTheme.DarkMode)
            startInfo.ArgumentList.Add("--dark");

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to start Imaging Manager:\n{ex.Message}",
                "Imaging Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RequestSystemPowerActionAsync(bool reboot)
    {
        if (_powerActionPending)
            return;

        _powerActionPending = true;
        try
        {
            MountedWimPowerProbeResult probe = await MountedWimPowerGuard.ProbeAsync();
            if (!probe.Success)
            {
                DialogResult verifyResult = MessageBox.Show(
                    _taskbar,
                    "Imaging Manager could not verify whether WIM images are currently mounted.\n\n" +
                    $"{probe.Error}\n\n" +
                    $"Continue and {(reboot ? "restart" : "shut down")} anyway?",
                    reboot ? "Restart - WIM Check Failed" : "Shutdown - WIM Check Failed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (verifyResult != DialogResult.Yes)
                    return;
            }
            else if (probe.Images.Count > 0)
            {
                bool imagingManagerAvailable = File.Exists(Path.Combine(AppContext.BaseDirectory, "Imaging.Manager.exe"));
                using MountedWimPowerGuardDialog dialog = new(probe.Images, reboot, imagingManagerAvailable);
                MountedWimPowerChoice choice = _taskbar != null
                    ? dialog.ShowGuardDialog(_taskbar)
                    : dialog.ShowGuardDialog();

                if (choice == MountedWimPowerChoice.OpenImagingManager)
                {
                    User32.AllowSetForegroundWindow(User32.ASFW_ANY);
                    LaunchImagingManager();
                    return;
                }

                if (choice != MountedWimPowerChoice.ContinueAnyway)
                    return;
            }

            RequestSystemPowerAction(reboot);
        }
        finally
        {
            _powerActionPending = false;
        }
    }

    private static void RequestSystemPowerAction(bool reboot)
    {
        if (SystemPower.TryRequestSystemPowerAction(reboot, out string? error))
            return;

        MessageBox.Show(
            $"Unable to {(reboot ? "restart" : "shut down")} the computer.\n\n{error}",
            reboot ? "Restart" : "Shutdown",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void DetachTaskbar(ShellTaskbarForm taskbar)
    {
        taskbar.OpenExplorerRequested -= Taskbar_OpenExplorerRequested;
        taskbar.BitLockerManagerRequested -= Taskbar_BitLockerManagerRequested;
        taskbar.ImagingManagerRequested -= Taskbar_ImagingManagerRequested;
        taskbar.ShutdownRequested -= Taskbar_ShutdownRequested;
        taskbar.RebootRequested -= Taskbar_RebootRequested;
        taskbar.FormClosed -= Taskbar_FormClosed;
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
            return;

        _isExiting = true;

        ShellTaskbarForm? taskbar = _taskbar;
        _taskbar = null;

        if (taskbar is not null)
        {
            DetachTaskbar(taskbar);

            try
            {
                if (!taskbar.IsDisposed)
                    taskbar.Dispose();
            }
            catch
            {
            }
        }

        base.ExitThreadCore();
    }
}
