WinPEGui for WinPE GUI Shell
============================

Purpose
-------
WinPEGui.exe is a small windowless supervisor intended to be launched by
winpeshl.ini. It starts Shell.Taskbar.Host.exe and FileManager.exe -host as
separate processes so the taskbar UI is not on the file-manager UI thread. It
restarts either process after non-clean exits.

The taskbar host still has a small fallback that can start/ensure
FileManager.exe -host if it is launched manually without WinPEGui, but
normal WinPE startup should let WinPEGui supervise both processes.

Startup C: policy
-----------------
Before the taskbar and file-manager processes start, WinPEGui normalizes the
WinPE C: assignment. It reuses the same BitLocker OS-volume signal and Windows
installation check used elsewhere in the shell. If exactly one Windows system
volume is found, that volume is assigned C:. If multiple candidates exist but
only one is on a fixed/internal disk, the fixed/internal candidate is assigned C:.
Physical USB attachment is determined with the same PnP-parent-chain logic File
Manager uses for its Eject option. SATA or NVMe drives behind USB/UASP bridges
therefore do not count as internal even when they report DriveType.Fixed or a
SCSI/SATA/NVMe storage bus. MSFT_Disk.BusType is used only as a fallback when
PnP ancestry cannot be resolved. If multiple candidates remain ambiguous and one
already owns C:, that plausible assignment is preserved.

When no primary Windows volume can be selected and C: belongs to an ordinary
non-Windows volume, WinPEGui moves that volume to an unused high drive letter so
C: remains reserved for a future Windows deployment. X: is never considered for
reassignment. USB-attached Windows disks may still be detected as Windows system
volumes; the USB ancestry only prevents one from winning the fixed/internal
tie-breaker over an installed disk.

The launcher will not move a volume that hosts WinPEGui, the configured taskbar
host, or FileManager executable. Drive-letter-policy decisions and failures are
written to the normal WinPEGui log and do not block shell startup.

Default processes
-----------------
The default settings file launches:

    Shell.Taskbar.Host.exe
    FileManager.exe -host

relative to the launcher directory. For deployment, place WinPEGui.exe,
WinPEGui.settings.json, Shell.Taskbar.Host.exe, and FileManager.exe in
the same shell folder or edit WinPEGui.settings.json to use the desired
paths.

Power handling
--------------
The taskbar host directly starts wpeutil.exe/shutdown.exe for Shutdown
and Reboot. The launcher also retains the legacy exit-code contract:

    0 = shutdown
    2 = reboot

When the launcher sees one of those clean shell exit codes, it starts the actual
power command itself and then keeps running while the system powers off/reboots.
This avoids the old WinPE behavior where returning from the shell/launcher could
let winpeshl.exe reboot the environment even for a shutdown request.

Configuration
-------------
WinPEGui.settings.json:

    Launcher.Shell.Path                 Taskbar host executable path. Relative
                                        paths are resolved against the launcher
                                        directory.
    Launcher.Shell.Args                 Optional taskbar host command-line
                                        arguments.
    Launcher.FileManager.Path           File-manager executable path. Relative
                                        paths are resolved against the launcher
                                        directory. If omitted, defaults to
                                        FileManager.exe.
    Launcher.FileManager.Args           File-manager command-line arguments. If
                                        omitted, defaults to -host.
    Launcher.FileManager.Restart        Whether the launcher should restart the
                                        file manager after a non-clean exit. If
                                        omitted, defaults to true.
    Launcher.RestartDelayMs             Delay before restarting after a crash.
                                        Valid range: 50-60000 ms; default: 500.
    Launcher.CrashBurstLimit            Number of crashes allowed inside the
                                        burst window before requesting shutdown.
                                        Valid range: 1-1000; default: 8.
    Launcher.CrashBurstWindowSeconds    Crash burst window length.
                                        Valid range: 1-86400 seconds; default: 30.
                                        Invalid values are logged and replaced
                                        with their defaults.
    Launcher.Log.Target                 label:WinPE, drive:E, an absolute folder,
                                        or fallback behavior.
    Launcher.Log.FileName               Log file name.

Debug overrides
---------------
You can override the configured taskbar host at runtime:

    WinPEGui.exe --shell "C:\Path\Shell.Taskbar.Host.exe"
    WinPEGui.exe --shell "Shell.Taskbar.Host.exe"
