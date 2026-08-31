# WinPE GUI Shell

WinPE GUI Shell is an independent, third-party graphical shell built from scratch specifically for customized Microsoft Windows Preinstallation Environment (Windows PE) images. It provides a file manager, desktop (non-interactive) and taskbar, file operations, file and folder pickers, BitLocker management utilities, and disk/partition imaging tools.

Rather than adapting a full Windows desktop shell or assembling the environment around a large collection of third-party tools, WinPE GUI Shell implements the shell experience itself. The result is a comparatively self-contained WinPE GUI with very few runtime dependencies while still providing a broad set of desktop, file-management, deployment, imaging, and recovery-oriented functions.

The published applications are self-contained .NET 8 executables; the shell itself does not require an installed .NET desktop runtime, `WinPE-NetFX`, or PowerShell. Additional WinPE components are needed only for the features that use them, such as WMI and BitLocker support.

The project is intended for deployment within a Windows PE image that the user creates independently. It is not designed as a replacement for File Explorer on a full Windows installation and is not affiliated with, endorsed by, or distributed by Microsoft.

## Trademarks

Windows and Windows PE are trademarks of the Microsoft group of companies. These names are used solely to identify the operating environment for which this software is designed. No Microsoft logos, graphical marks, system files, or other Microsoft-owned assets are included with this project.

## Features

- File browsing with tree and list views.
- Copy, move, permanent delete, rename, conflict resolution, and progress dialogs.
- Desktop, taskbar, Start menu, clock, and application task buttons.
- Per-monitor DPI handling for the shell and file-operation dialogs.
- File and folder picker helper for scripts and external applications.
- BitLocker volume status, unlock, lock, and management utilities.
- Physical-disk FFU capture and apply operations.
- Partition-scoped WIM capture, append, and apply operations.
- BitLocker-aware imaging status, volume icons, and direct unlock integration.
- Windows RE staging support when capturing an installed Windows partition to WIM.
- Light and dark shell themes.
- Windowless supervisor that starts and monitors the taskbar and file-manager processes.
- Purpose-built WinPE shell architecture with minimal runtime dependencies.

## Architecture

The source tree is divided into executable and supporting library projects:

| Project | Purpose |
| --- | --- |
| `WinPEGui` | Windowless supervisor intended to be started by `winpeshl.ini`. Builds `WinPEGui.exe`. |
| `Shell.Taskbar.Host` | Hosts the desktop, taskbar, Start menu, and task-window tracking. |
| `Explorer.Host` | Builds `FileManager.exe`, which hosts file-manager windows, file operations, and the picker service. |
| `ExplorerPicker` | Command-line client for open-file, save-file, and folder-selection dialogs. |
| `BitLocker.Manager` | Administrative BitLocker volume-management interface. |
| `BitLocker.Unlock` | Per-volume BitLocker unlock prompt. |
| `Imaging.Manager` | Physical-disk and partition imaging interface for FFU and WIM operations. |
| `Shell.Core` | Shared models, contracts, launch requests, and picker IPC. |
| `Shell.Infrastructure` | File-system, drive-state, file-association, and window-coordination services. |
| `Explorer.UI` | File-manager window and navigation user interface. |
| `Shell.Taskbar` | Desktop and taskbar user interface. |
| `Shared.Shell` | Shared Win32 helpers, theming, icons, and shell utilities. |
| `BitLocker.Core` | BitLocker backends, state models, and activation helpers. |
| `Imaging.Core` | Physical-disk inventory, DISM FFU/WIM backends, imaging preflight logic, temporary drive-letter handling, and Windows RE staging. |

`WinPEGui.exe` normally starts and supervises:

```text
Shell.Taskbar.Host.exe
FileManager.exe -host
```

The taskbar and file-manager host remain separate processes so file-manager work does not block the taskbar UI thread. Companion utilities such as BitLocker Manager and Imaging Manager are launched on demand from the shell.

## Requirements

### Development system

- Windows 10 or later, x64.
- Visual Studio 2022 with the .NET desktop development workload, or the .NET 8 SDK.
- The `WinPEGui.sln` solution included at the repository root.
- Windows ADK and the matching Windows PE add-on when creating or servicing a WinPE image.

The repository includes `global.json` to keep command-line builds on the .NET 8 SDK family.

### Target WinPE image

The published applications target `win-x64`. The target environment must therefore be an x64 Windows PE image with the drivers and optional components needed by the features being used.

For the complete shell, the expected configuration includes:

- A current x64 Windows PE image created from the Windows ADK and matching Windows PE add-on.
- `WinPE-WMI`.
- `WinPE-SecureStartup`.
- A matching `imageres.dll.mun` supplied from the user's own properly licensed Windows installation or deployment media and added to the WinPE image. This should be treated as required for the intended shell icon set and complete UI appearance.
- Required storage, USB, network, display, and other hardware drivers for the target systems.
- Administrative execution for BitLocker management, imaging, and other privileged shell operations.

`WinPE-WMI` is used by the shell for WMI-based device, physical-disk, drive-state, and BitLocker-related operations.

`WinPE-SecureStartup` provides BitLocker and TPM support, including the BitLocker command-line tools and WMI management libraries. Install `WinPE-WMI` before `WinPE-SecureStartup`.

The BitLocker status backend expects `manage-bde.exe`, which is supplied by the WinPE Secure Startup component.

Imaging Manager uses the Windows deployment tools available in WinPE, including `DISM.exe` for FFU/WIM operations and `DiskPart.exe` when a temporary drive letter is required. Automatic Windows RE staging also requires `reagentc.exe` to be available either in WinPE or in the selected offline Windows installation.

The applications are published as self-contained .NET 8 executables. The target image does not require the .NET desktop runtime or `WinPE-NetFX` solely to run WinPE GUI Shell.

When adding WinPE optional components, use packages that match the architecture and ADK build of the WinPE image. Where applicable, also add the corresponding language package.

### Minimum and optional functionality

| Component | Needed for |
| --- | --- |
| Base x64 WinPE | Starting the shell, basic local file management, and Windows deployment tools such as DISM/DiskPart. |
| `WinPE-WMI` | Full drive monitoring, physical-disk inventory, WMI device operations, and BitLocker state integration. |
| `WinPE-SecureStartup` | BitLocker status, unlock, lock, management functionality, and BitLocker-aware imaging integration. |
| `imageres.dll.mun` from a licensed Windows source | Intended Windows-style shell imagery, including the Start button icon and other icons used by the taskbar, Start menu, file manager, BitLocker Manager, and Imaging Manager. |
| Network drivers and WinPE networking | Network shares, mapped drives, and network tools. |
| PowerShell optional components | Only external scripts or workflows that specifically require Windows PowerShell. The shell itself does not require PowerShell. |
| Additional font/language packages | Languages or scripts not present in the base image. |

Features whose supporting WinPE components are absent may be unavailable or fail when invoked. The complete supported configuration should include both `WinPE-WMI` and `WinPE-SecureStartup`.

## Imaging Manager

Imaging Manager is a companion application for physical-disk and partition imaging from WinPE. Its selection model intentionally separates whole-disk FFU operations from partition-scoped WIM operations.

### Physical disk selected

When only a physical disk is selected, Imaging Manager exposes:

- **Capture FFU** using DISM `/Capture-FFU`.
- **Apply FFU** using DISM `/Apply-FFU`.
- Physical-disk, partition, and BitLocker suitability information.

FFU operates on the complete physical disk. Applying an FFU overwrites the target disk rather than an individual partition.

Imaging Manager checks BitLocker state before FFU capture and strongly warns when encrypted volumes are present. Unlocking or suspending BitLocker does not decrypt the sectors on disk; a fully decrypted source is preferred before FFU capture. BitLocker status that cannot be determined is treated as unknown rather than as safe.

### Partition selected

Selecting a partition changes the active scope to that partition. FFU actions are disabled and the partition action row exposes:

- **Capture WIM**
- **Apply WIM**
- **Unlock** when the selected volume is BitLocker-locked

Partition tiles use the same BitLocker-aware drive icon states used by the file manager and BitLocker Manager, including locked, unlocked/protected, protection-off, system-drive, and unknown-state variants where applicable.

The Unlock action reuses `BitLocker.Unlock.exe`; Imaging Manager does not duplicate password/recovery-key handling.

### Capture WIM

Capture WIM uses DISM `/Capture-Image` against the selected partition.

Any selected partition may be offered for WIM capture. If the partition does not currently have a drive letter, Imaging Manager attempts to assign an unused temporary drive letter with DiskPart and removes it after the operation. Partitions without a mountable filesystem may still fail when Windows/DiskPart cannot expose them; the UI does not preemptively prohibit the attempt solely because the partition is hidden or normally unlettered.

If the selected destination WIM does not exist, a new WIM is created.

If the destination WIM already exists, Imaging Manager offers:

- **Replace** — delete the existing WIM and create a new image starting at index 1.
- **Append** — preserve the existing WIM and add the new capture as another image index using DISM `/Append-Image`.
- **Cancel**.

Append modifies the existing WIM in place. Ensure the destination has sufficient free space before appending.

#### Windows RE staging

When the selected partition contains a Windows installation, Imaging Manager checks for:

```text
<Windows drive>:\Windows\System32\Recovery\winre.wim
```

If `winre.wim` is already present, capture proceeds normally. This makes the same Capture WIM path suitable for prepared/golden images that already contain their recovery image.

If `winre.wim` is missing, Imaging Manager offers to retrieve the configured Windows RE image before capture. The staging process:

1. Queries the selected offline Windows installation with REAgentC to determine its configured Windows RE location.
2. Temporarily assigns the configured Recovery partition an unused drive letter.
3. Copies its `winre.wim` into the selected Windows installation's `Windows\System32\Recovery` directory.
4. Removes the temporary Recovery-partition drive letter.
5. Captures the Windows partition to WIM.
6. Removes the temporarily staged `winre.wim` from the source Windows partition afterward.

A `winre.wim` that existed before capture is never removed by Imaging Manager.

If WinRE cannot be staged, Imaging Manager reports the failure and requires an explicit choice before capturing without it. Cleanup failures for temporary drive letters or a temporarily staged `winre.wim` are surfaced rather than silently ignored.

### Apply WIM

Apply WIM uses DISM `/Apply-Image` against the currently selected partition.

After a WIM is selected, Imaging Manager reads its image information and allows the desired image/index to be selected before applying it.

The current Apply WIM operation is deliberately a regular partition-scoped DISM apply. It does **not**:

- format the target partition;
- repartition the disk;
- recreate EFI, MSR, or Recovery partitions;
- run BCDBoot;
- configure Windows RE; or
- modify neighboring partitions as part of a deployment workflow.

Because the target is not formatted first, files already present on the target partition that are not replaced by the WIM may remain after the apply. A future full deployment/clean-restore workflow should remain a separate operation from this regular Apply WIM behavior.

### Imaging safety checks

Current imaging safeguards include:

- FFU capture cannot be written to the same physical disk being captured.
- FFU apply cannot consume an FFU stored on the target disk.
- FFU apply is blocked when Imaging Manager itself is running from the target disk.
- WIM capture cannot be saved onto the partition being captured.
- WIM apply prevents using a source WIM stored on the selected target partition.
- Imaging Manager avoids deleting an existing WIM if an append operation fails or is canceled.
- Temporary source/target/Recovery drive-letter cleanup failures are surfaced to the user.
- Temporarily staged WinRE files are removed after WIM capture when Imaging Manager added them.

Imaging operations are destructive by nature. Verify the selected physical disk, partition, source image, and destination before starting an operation.

## Microsoft system resources are not included

This repository does not distribute:

- Windows PE or any Windows image.
- Microsoft executables, DLLs, MUI files, or resource packages.
- Microsoft logos, icons, or other graphical assets.
- `imageres.dll`, `imageres.dll.mun`, or equivalent Windows system resources.

The shell intentionally uses Windows system image resources for familiar file, folder, drive, Start-menu, and shell imagery without redistributing those Microsoft-owned resources. The current code loads icons from system resource DLLs such as `imageres.dll` and `shell32.dll`.

For the intended shell UI, `imageres.dll.mun` should be treated as a required deployment file. It is not included in this repository and must be supplied from the user's own properly licensed Windows installation or deployment media. On current Windows resource layouts, copy the matching file into the mounted WinPE image as:

```text
%SystemRoot%\SystemResources\imageres.dll.mun
```

The file should match the Windows/WinPE build, architecture, and language resources being used. If the corresponding `imageres.dll` is not already present in the target image, it must likewise be supplied from an appropriate matching Windows source.

The shell can still start when these image resources are absent, and generic fallback behavior exists for some requested icons. However, the UI is incomplete in that state: icons sourced from `imageres.dll` will be missing or degraded. This includes the Start button icon, which the shell loads directly from `imageres.dll`, along with other Start-menu, taskbar, file-manager, drive, and companion-utility imagery.

In practice, `imageres.dll.mun` should be copied into the WinPE image as part of a normal deployment rather than treated as an optional cosmetic addition.

Do not submit Microsoft system files to this repository or include them with a project release.

## Build

Open `WinPEGui.sln` in Visual Studio and select:

```text
Release | Any CPU
```

The solution configuration is `Any CPU`, while the executable projects themselves target x64 and publish for `win-x64`.

Then build the solution normally.

From a Developer PowerShell prompt:

```powershell
dotnet restore .\WinPEGui.sln
dotnet build .\WinPEGui.sln --configuration Release --no-restore
```

## Publish

Each executable project includes a checked-in Visual Studio publish profile:

```text
Properties\PublishProfiles\FolderProfile.pubxml
```

The profiles use:

- Configuration: `Release`
- Platform: `Any CPU`
- Target framework: `net8.0-windows`
- Runtime: `win-x64`
- Self-contained: enabled
- Single-file publishing: enabled
- Trimming: disabled by the project configuration
- ReadyToRun: disabled

### Visual Studio

Right-click each executable project and select **Publish**, then use the existing `FolderProfile` profile.

The executable projects that need to be published are:

```text
WinPEGui
Shell.Taskbar.Host
Explorer.Host
ExplorerPicker
BitLocker.Manager
BitLocker.Unlock
Imaging.Manager
```

Each profile publishes beneath its own project directory:

```text
bin\Release\net8.0-windows\publish\win-x64\
```

Although the source project remains named `Explorer.Host`, its published executable is:

```text
FileManager.exe
```

### Command line

The same checked-in profiles can be used from PowerShell:

```powershell
dotnet publish .\WinPEGui\WinPEGui.csproj -p:PublishProfile=FolderProfile
dotnet publish .\Shell.Taskbar.Host\Shell.Taskbar.Host.csproj -p:PublishProfile=FolderProfile
dotnet publish .\Explorer.Host\Explorer.Host.csproj -p:PublishProfile=FolderProfile
dotnet publish .\ExplorerPicker\ExplorerPicker.csproj -p:PublishProfile=FolderProfile
dotnet publish .\BitLocker.Manager\BitLocker.Manager.csproj -p:PublishProfile=FolderProfile
dotnet publish .\BitLocker.Unlock\BitLocker.Unlock.csproj -p:PublishProfile=FolderProfile
dotnet publish .\Imaging.Manager\Imaging.Manager.csproj -p:PublishProfile=FolderProfile
```

For deployment, place the resulting shell files together unless `WinPEGui.settings.json` specifies different locations:

```text
WinPEGui.exe
WinPEGui.settings.json
Shell.Taskbar.Host.exe
FileManager.exe
ExplorerPicker.exe
BitLocker.Manager.exe
BitLocker.Unlock.exe
Imaging.Manager.exe
```

The publish output may also contain supporting files intentionally copied by individual projects, such as `README-WinPEGui.txt`.

The file-manager host, taskbar host, BitLocker Manager, and Imaging Manager locate companion executables relative to their own application directory. Imaging Manager's integrated Unlock action therefore expects `BitLocker.Unlock.exe` to be deployed alongside it.

## WinPE startup

A ready-to-use `winpeshl.ini` is included at the repository root. For a standard deployment matching the checked-in file, place the shell under:

```text
%SYSTEMROOT%\WinpeGUI\
```

and copy the repository's `winpeshl.ini` into the mounted WinPE image at:

```text
%SYSTEMROOT%\System32\winpeshl.ini
```

The checked-in file currently contains:

```ini
[LaunchApps]
%SYSTEMROOT%\System32\wpeinit.exe
"%SYSTEMROOT%\WinpeGUI\WinPEGui.exe"
```

This allows WinPE initialization to run first and then starts the top-level `WinPEGui.exe` supervisor. If the shell is deployed to a different directory, edit the executable path in `winpeshl.ini` to match.

`WinPEGui.exe` is windowless and normally supervises:

```text
Shell.Taskbar.Host.exe
FileManager.exe -host
```

`WinPEGui.settings.json` controls the executable paths, arguments, restart behavior, crash-burst handling, and log destination.

See:

```text
WinPEGui\README-WinPEGui.txt
```

for the launcher configuration reference.

## Launcher configuration

The default `WinPEGui.settings.json` launches the taskbar and file manager relative to the directory containing `WinPEGui.exe`.

The default configuration uses:

```text
Shell.Taskbar.Host.exe
FileManager.exe -host
```

The supervisor can restart either process after an unexpected exit and includes crash-burst protection to avoid an uncontrolled restart loop.

Launcher logging can target a labeled volume, drive, or configured directory. See `README-WinPEGui.txt` for the available settings and valid ranges.

## Theme arguments

The shell executables recognize:

```text
--dark
--light
--theme dark
--theme light
```

When using `WinPEGui`, apply the desired theme arguments to both the taskbar host and file-manager host in `WinPEGui.settings.json` so the shell uses a consistent theme.

## File picker

`ExplorerPicker.exe` communicates with the running file-manager host and writes the selected path to standard output. It can optionally write the result to a file.

Examples:

```text
ExplorerPicker.exe --openfile [--initial <path>] [--title <title>] [--filter <exts>]
ExplorerPicker.exe --savefile [--initial <path>] [--title <title>] [--filter <exts>]
ExplorerPicker.exe --selectfolder [--initial <path>] [--title <title>]
```

Run:

```text
ExplorerPicker.exe --help
```

for the complete option list and exit-code behavior.

## Pre-release checks

Before creating a release, perform a clean build and publish of all executable projects, then test the resulting applications in the actual target WinPE image.

Important scenarios include:

- Starting the shell through `winpeshl.ini`.
- Supervisor, taskbar, and file-manager startup and restart behavior.
- Shutdown and reboot from the shell.
- Operation with the intended `WinPE-WMI` and `WinPE-SecureStartup` components.
- Behavior when required optional components are absent.
- BitLocker status and management.
- Passphrase unlock.
- Recovery-password unlock.
- Recovery-key-file unlock.
- BitLocker volume locking.
- Imaging Manager physical-disk and partition enumeration.
- FFU capture to a different physical disk.
- FFU apply and destructive-target confirmation.
- FFU capture warnings for encrypted, partially encrypted, and BitLocker-status-unknown disks.
- WIM capture of normal data partitions.
- WIM capture of hidden/mountable partitions using temporary drive letters.
- Windows-partition WIM capture with `winre.wim` already present.
- Windows-partition WIM capture with WinRE temporarily staged from the configured Recovery partition.
- WinRE staging failure and cleanup-warning paths.
- WIM replace and append-to-existing-WIM behavior.
- Multi-index WIM enumeration and Apply WIM image selection.
- Regular Apply WIM behavior without formatting or repartitioning.
- Imaging Manager BitLocker-aware partition icons and integrated Unlock action.
- Temporary drive-letter cleanup after success, failure, and cancellation.
- DPI changes between monitors and scaling levels.
- Copy and move operations.
- Conflict handling.
- Cancel, retry, and skip paths.
- Permanent deletion.
- Junction and symbolic-link handling.
- Removable drives.
- Optical drives.
- Network drives and mapped shares.
- BitLocker-locked and otherwise unavailable volumes.
- File and folder picker calls from intended scripts or applications.
- Presence of the user-supplied `imageres.dll.mun` resource and correct Start/taskbar/file-manager/companion-utility icon rendering.
- Degraded/fallback behavior when user-supplied Windows image resources are intentionally omitted.

## License

WinPE GUI Shell is source-available under the PolyForm Noncommercial License 1.0.0.

Noncommercial use is permitted under the license. Commercial or other for-profit use requires separate permission from the copyright holder.

See `LICENSE.md` for the complete terms.
