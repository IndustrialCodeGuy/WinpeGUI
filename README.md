# WinPE GUI Shell

WinPE GUI Shell is an independent, third-party graphical shell for customized Microsoft Windows Preinstallation Environment (Windows PE) images. It provides a file manager, desktop and taskbar, file operations, file and folder pickers, and BitLocker management utilities.

The project is intended for deployment within a Windows PE image that the user creates independently. It is not designed as a replacement for File Explorer on a full Windows installation and is not affiliated with, endorsed by, or distributed by Microsoft.

## Trademarks

Windows and Windows PE are trademarks of the Microsoft group of companies. These names are used solely to identify the operating environment for which this software is designed. No Microsoft logos, graphical marks, system files, or other Microsoft-owned assets are included with this project.

## Features

- Explorer-style file browsing with tree and list views.
- Copy, move, permanent delete, rename, conflict resolution, and progress dialogs.
- Desktop, taskbar, Start menu, clock, and application task buttons.
- Per-monitor DPI handling for the shell and file-operation dialogs.
- File and folder picker helper for scripts and external applications.
- BitLocker volume status, unlock, lock, and management helpers.
- Light and dark shell themes.
- A windowless launcher that supervises the taskbar and file-manager processes.

## Architecture

The current source tree is split into several executable and library projects:

| Project | Purpose |
| --- | --- |
| `WinPEGui` | Windowless supervisor intended to be started by `winpeshl.ini`. |
| `Shell.Taskbar.Host` | Hosts the desktop, taskbar, Start menu, and task-window tracking. |
| `Explorer.Host` | Builds `FileManager.exe`, which hosts file-manager windows, file operations, and the picker service. |
| `ExplorerPicker` | Command-line client for open-file, save-file, and folder-selection dialogs. |
| `BitLocker.Manager` | Administrative BitLocker volume-management interface. |
| `BitLocker.Unlock` | Per-volume BitLocker unlock prompt. |
| `Shell.Core` | Shared models, contracts, launch requests, and picker IPC. |
| `Shell.Infrastructure` | File-system, drive-state, file-association, and window-coordination services. |
| `Explorer.UI` | File-manager window and navigation user interface. |
| `Shell.Taskbar` | Desktop and taskbar user interface. |
| `Shared.Shell` | Shared Win32 helpers, theming, icons, and shell utilities. |
| `BitLocker.Core` | BitLocker backends, state models, and activation helpers. |

`WinPEGui.exe` normally starts and supervises:

```text
Shell.Taskbar.Host.exe
FileManager.exe -host
```

The taskbar and file-manager host remain separate so long-running file-manager work does not block the taskbar UI thread.

## Requirements

### Development system

- Windows 10 or later, x64.
- Visual Studio 2022 with the **.NET desktop development** workload, or the .NET 8 SDK.
- The existing solution file at the repository root.
- The Windows ADK and Windows PE add-on when building or servicing a WinPE image.

### Target WinPE image

The published applications target `win-x64`. The target image must therefore be an x64 Windows PE image with the drivers and optional components required by the features being used.

Minimum requirements for the complete shell are:

- A current x64 WinPE image created from the Windows ADK and matching Windows PE add-on.
- `WinPE-WMI`, required by the shell's WMI-based drive discovery, drive-state monitoring, device handling, and BitLocker capability checks.
- `WinPE-SecureStartup`, required for BitLocker provisioning and management support, the BitLocker command-line tools, and the BitLocker WMI management libraries. Add `WinPE-WMI` before `WinPE-SecureStartup`.
- Storage, USB, network, display, and other hardware drivers required by the target systems.
- Administrative execution for BitLocker management and other privileged shell operations.
- `manage-bde.exe`, which is expected by the BitLocker status backend and is supplied by the WinPE Secure Startup component.

The applications publish as self-contained .NET 8 executables. The target image does **not** need the .NET desktop runtime or the `WinPE-NetFX` optional component solely to run this project.

When adding WinPE optional components, use packages from the same ADK build and architecture as the WinPE image. Add the corresponding language package for each optional component when the image uses a localized language.

### Minimum and optional functionality

| Component | Needed for |
| --- | --- |
| Base x64 WinPE | Starting the shell and basic local file management. |
| `WinPE-WMI` | Full drive monitoring, WMI device operations, and BitLocker state integration. |
| `WinPE-SecureStartup` | BitLocker status, unlock, lock, and management functionality. |
| Network drivers and WinPE networking | Network shares, mapped drives, and network tools. |
| PowerShell optional components | Only scripts or workflows that specifically depend on Windows PowerShell; the shell itself does not require PowerShell. |
| Additional font/language packages | Non-default languages and scripts not present in the base image. |

Features whose required WinPE component is absent may fail or be unavailable. The complete supported configuration should include both `WinPE-WMI` and `WinPE-SecureStartup`.

## Microsoft system resources are not included

This repository does not distribute:

- Windows PE or any Windows image.
- Microsoft executables, DLLs, MUI files, or resource packages.
- Microsoft logos, icons, or other graphical assets.
- `imageres.dll`, `imageres.dll.mun`, or equivalent Windows system resources.

The shell references `%SystemRoot%\System32\imageres.dll` for familiar system file, folder, drive, and shell imagery. On current Windows builds, the corresponding resources may be stored in the matching `imageres.dll.mun` file under the Windows system-resource directory.

Users who want those system icons must supply the appropriate files from their own properly licensed Windows installation or deployment media and are responsible for ensuring that the files match the WinPE build and language. Do not submit Microsoft system files to this repository or attach them to a release.

The shell contains generic fallback behavior when a requested system icon cannot be loaded, but visual fidelity will be reduced when the matching Windows resources are absent.

## Build

Open the existing solution in Visual Studio, select **Release** and **x64**, and build the solution.

From a Developer PowerShell prompt, the equivalent command is:

```powershell
dotnet restore .\WinPEGui.sln
dotnet build .\WinPEGui.sln --configuration Release --no-restore
```


## Publish

The project files contain the intended publish settings:

- Runtime: `win-x64`
- Self-contained: enabled
- Single file: enabled
- Trimming: disabled
- ReadyToRun: disabled

Publish the executable projects with:

```powershell
$projects = @(
    'WinPEGui/WinPEGui.csproj',
    'Shell.Taskbar.Host/Shell.Taskbar.Host.csproj',
    'Explorer.Host/Explorer.Host.csproj',
    'ExplorerPicker/ExplorerPicker.csproj',
    'BitLocker.Manager/BitLocker.Manager.csproj',
    'BitLocker.Unlock/BitLocker.Unlock.csproj'
)

foreach ($project in $projects) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
    dotnet publish $project `
        --configuration Release `
        --output "artifacts/publish/$name"
}
```

For deployment, place these files in the same shell directory unless the launcher settings specify other paths:

```text
WinPEGui.exe
WinPEGui.settings.json
Shell.Taskbar.Host.exe
FileManager.exe
ExplorerPicker.exe
BitLocker.Manager.exe
BitLocker.Unlock.exe
```

The file-manager host, taskbar host, and BitLocker manager locate their companion executables relative to their own application directory.

## WinPE startup

Configure `winpeshl.ini` to launch the top-level supervisor. With the current executable name, a typical entry is:

```ini
[LaunchApp]
AppPath = %SYSTEMDRIVE%\Shell\WinPEGui.exe
```

Adjust the path to match the location used in the image.

`WinPEGui.settings.json` controls the taskbar host, file-manager host, restart behavior, crash-burst handling, and log destination. See `WinPEGui/README-WinPEGui.txt` for every setting.

## Theme arguments

The shell executables recognize:

```text
--dark
--light
--theme dark
--theme light
```

When using the launcher, place the desired theme argument in the configured taskbar and file-manager-host arguments so both processes use the same theme.

## File picker

`ExplorerPicker.exe` communicates with the running file-manager host and writes the selected path to standard output. It can optionally write the result to a file.

```text
ExplorerPicker.exe --openfile [--initial <path>] [--title <title>] [--filter <exts>]
ExplorerPicker.exe --savefile [--initial <path>] [--title <title>] [--filter <exts>]
ExplorerPicker.exe --selectfolder [--initial <path>] [--title <title>]
```

Run `ExplorerPicker.exe --help` for the complete option list and exit-code behavior.

## Pre-release checks

GitHub Actions restores and builds the existing root solution, checks NuGet dependencies for known vulnerabilities, and publishes all executable projects on `windows-latest`.

Before creating a release, test the published applications in the actual target WinPE image. Important scenarios include:

- Starting the shell through `winpeshl.ini`.
- Supervisor, taskbar, and file-manager restart behavior.
- Behavior when `WinPE-WMI` or `WinPE-SecureStartup` is missing.
- BitLocker status, passphrase unlock, recovery-password unlock, recovery-key-file unlock, and locking.
- DPI changes between monitors and scaling levels.
- Copy, move, conflict, cancel, retry, skip, and permanent-delete paths.
- Junction and symbolic-link handling.
- Removable, optical, network, BitLocker-locked, and unavailable drives.
- Behavior with and without the user-supplied Windows icon resources.
- Picker calls from the scripts or applications that will use them.
- Shutdown and restart from the shell.

## License

WinPE GUI Shell is source-available under the **PolyForm Noncommercial License 1.0.0**. Noncommercial use is permitted under the license. Commercial or other for-profit use requires separate permission from the copyright holder.

See [LICENSE.md](LICENSE.md) for the complete terms.
