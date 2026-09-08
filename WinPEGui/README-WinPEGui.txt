WinPEGui for WinPE GUI Shell
============================

Purpose
-------
WinPEGui.exe is a small windowless supervisor intended to be launched by
winpeshl.ini. It starts Shell.Taskbar.Host.exe and FileManager.exe -host as
separate processes so the taskbar UI is not on the file-manager UI thread. It
restarts either process after non-clean exits.

The taskbar host can still start FileManager.exe -host on demand if the user
selects File Manager and no file-manager host is available, but normal WinPE
startup lets WinPEGui supervise both processes.

Default processes
-----------------
The default settings file launches:

    Shell.Taskbar.Host.exe
    FileManager.exe -host

relative to the launcher directory. For deployment, place WinPEGui.exe,
WinPEGui.settings.json, Shell.Taskbar.Host.exe, and FileManager.exe in
the same shell folder or edit WinPEGui.settings.json to use the desired
paths.

Required WinPE profile
----------------------
When running in WinPE, WinPEGui validates the supported optional-component
profile before starting the taskbar and file-manager hosts. The release image
is expected to include:

    WinPE-WMI
    WinPE-NetFX
    WinPE-Scripting
    WinPE-PowerShell
    WinPE-StorageWMI
    WinPE-SecureStartup

The validation checks base WMI, manage-bde.exe and the BitLocker WMI provider,
PowerShell, and the MSFT_Disk/MSFT_Partition Storage WMI providers. If the
profile is incomplete or unusable, WinPEGui displays a configuration error,
logs the failure, and does not start a reduced-function shell.

Full-Windows development runs do not perform this WinPE-image requirements
check.

Power handling
--------------
The taskbar host directly starts the native WinPE wpeutil.exe for Shutdown and
Reboot. The launcher also uses wpeutil.exe for guarded fatal-startup and
crash-storm shutdown requests, then keeps running while the system powers off.
This avoids returning control to winpeshl.exe before the requested power action
has taken effect.

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
