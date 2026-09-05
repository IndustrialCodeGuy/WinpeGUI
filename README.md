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
- WIM capture/append, clean partition apply, whole-disk deployment, export, mount/unmount, and offline driver injection.
- BitLocker-aware imaging status, volume icons, and direct unlock integration.
- Windows RE staging support when capturing an installed Windows partition to WIM.
- Imaging Manager integration in the Start menu when `Imaging.Manager.exe` is deployed alongside the shell.
- Light and dark shell themes.
- Windowless supervisor that starts and monitors the taskbar and file-manager processes.
- Shutdown/reboot guard that detects registered mounted WIMs and prompts to resolve them before leaving WinPE.
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
| `Imaging.Manager` | Physical-disk and partition imaging and offline WIM-servicing interface. |
| `Shell.Core` | Shared models, contracts, launch requests, and picker IPC. |
| `Shell.Infrastructure` | File-system, drive-state, file-association, and window-coordination services. |
| `Explorer.UI` | File-manager window and navigation user interface. |
| `Shell.Taskbar` | Desktop and taskbar user interface. |
| `Shared.Shell` | Shared Win32 helpers, theming, icons, and shell utilities. |
| `BitLocker.Core` | BitLocker backends, state models, and activation helpers. |
| `Imaging.Core` | Physical-disk inventory, DISM FFU/WIM backends, WIM deployment and servicing logic, imaging preflight logic, partition formatting, temporary drive-letter handling, and Windows RE staging. |

`WinPEGui.exe` normally starts and supervises:

```text
Shell.Taskbar.Host.exe
FileManager.exe -host
```

The taskbar and file-manager host remain separate processes so file-manager work does not block the taskbar UI thread. Companion utilities such as BitLocker Manager and Imaging Manager are launched on demand from the shell.

When `Imaging.Manager.exe` is present in the shell application directory, the Start menu automatically exposes an **Imaging Manager** entry. The entry follows the active shell theme and launches the companion application from that same directory. If the executable is omitted, the menu item is omitted as well.

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

Imaging Manager uses the Windows deployment tools available in WinPE, including `DISM.exe`, `DiskPart.exe`, and BCDBoot where appropriate. Automatic Windows RE staging/configuration also requires `reagentc.exe` to be available either in WinPE or in the selected/applied offline Windows installation.

Imaging Manager uses `ExplorerPicker.exe` for its file and folder selection dialogs and reuses `BitLocker.Unlock.exe` for integrated volume unlock operations. Those companion executables should be deployed alongside `Imaging.Manager.exe`.

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

Imaging Manager is a companion application for physical-disk, partition, and WIM-file operations from WinPE. The main view uses a Disk Management-style layout: each physical disk has its own compact disk selector showing size and online/offline state beside a proportional partition strip, followed by a separate strip for currently mounted WIM images. Only one disk, partition, or mounted WIM owns the active selection at a time.

The main command layout is split into two full-width strips:

- A **global command strip** above the disks contains **Mount WIM** and **Export WIM** on the left, with **Refresh** pinned to the right. **Cleanup Mounts** remains visible on the left as a system-wide recovery action and is enabled only when DISM reports one or more **Invalid** mounted WIMs. These commands do not depend on the active disk/partition/WIM selection and leave room for additional global actions later.
- A **contextual command strip** below the disk/WIM selector shows the current target on the left and only the actions that apply to that selection on the right.

The contextual actions are:

```text
Disk selected:
Get Info
Capture FFU
Apply FFU
Deploy WIM

Partition selected:
Get Info
Capture WIM
Apply WIM
Add Drivers    (recognized offline Windows installation only)
Unlock         (locked BitLocker partition only)

Mounted WIM selected (status OK):
Get Info
Unmount WIM
Add Drivers

Mounted WIM selected (Needs Remount):
Get Info
Remount WIM

Mounted WIM selected (Invalid):
Get Info
```

Availability still follows the current disk/partition/mounted-WIM selection, BitLocker state, and DISM mounted-image inventory. A read-only mounted WIM keeps **Add Drivers** visible but disabled. **Get Info** opens the detailed information for the current selection instead of keeping a permanent information pane on screen.

### Physical disk and FFU operations

Each physical disk is shown as a compact selector beside its partition strip. When the disk selector itself is active, Imaging Manager exposes whole-disk FFU/deployment operations:

- Capture FFU using DISM `/Capture-FFU`.
- Apply FFU using DISM `/Apply-FFU`.
- Physical-disk, partition, and BitLocker suitability information.

FFU operates on the complete physical disk. Applying an FFU overwrites the target disk rather than an individual partition.

Imaging Manager checks BitLocker state before FFU capture and strongly warns when encrypted volumes are present. Unlocking or suspending BitLocker does not decrypt the sectors on disk; a fully decrypted source is preferred before FFU capture. BitLocker status that cannot be determined is treated as unknown rather than as safe.

### Partition selection and BitLocker integration

Every disk row shows its partitions at the same time. Partition widths are proportional to partition size with a minimum clickable width for small EFI/MSR/Recovery partitions, without a per-disk or overall horizontal scrollbar. Each tile begins with the partition number (and drive letter when assigned). Lettered partitions show total and used space on separate lines; unlettered partitions show total size only. Selecting a partition gives that partition the single active highlight and changes the active partition scope: FFU/deployment actions are disabled, while partition-scoped WIM capture/apply operations can target that volume.

Disk and partition selectors retain the shell's familiar small system-resource icons. Partition tiles use the same BitLocker-aware drive icon states used by the file manager and BitLocker Manager, including locked, unlocked/protected, protection-off, system-drive, and unknown-state variants where applicable.

The **Unlock** action reuses `BitLocker.Unlock.exe`; Imaging Manager does not duplicate password/recovery-key handling.

### Capture WIM

Capture WIM uses DISM `/Capture-Image` against the selected partition.

Any selected partition may be offered for WIM capture. If the partition does not currently have a drive letter, Imaging Manager attempts to assign an unused temporary drive letter with DiskPart and removes it after the operation. Partitions without a mountable filesystem may still fail when Windows/DiskPart cannot expose them; the UI does not preemptively prohibit the attempt solely because the partition is hidden or normally unlettered.

New WIM captures explicitly use DISM `/Compress:max`.

If the selected destination WIM does not exist, a new WIM is created.

If the destination WIM already exists, Imaging Manager offers:

- **Replace** — delete the existing WIM and create a new image starting at index 1.
- **Append** — preserve the existing WIM and add the new capture as another image index using DISM `/Append-Image`.
- **Cancel**.

Append modifies the existing WIM in place. The appended image uses the compression type already established by the existing WIM. Ensure the destination has sufficient free space before appending.

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

Before DISM starts, the selected target volume is quick-formatted as NTFS. The format is directed at the exact active drive letter and verified with a temporary marker file. If the marker survives the format, Imaging Manager treats the format as failed and does not start DISM. This prevents an unsuccessful format from silently turning into an apply over an existing Windows tree.

Apply WIM remains partition-scoped. It does not repartition the disk or intentionally modify neighboring partitions as part of the restore.

The confirmation dialog includes **Configure Windows boot files after apply (BCDBoot)**. The option defaults on only when the pre-format target contains a recognizable Windows installation at:

```text
Windows\System32\Config\SYSTEM
```

Blank, data, and test partitions therefore default to leaving the machine boot configuration unchanged. The user can override the option in either direction before starting the apply.

The same confirmation dialog also includes **Assign the target partition to C: before applying the image**. It is off by default, so Apply WIM normally preserves the target partition's current WinPE drive letter (or uses the normal temporary-letter path for an unlettered partition). When explicitly selected, Imaging Manager makes C: available, reassigns the chosen target to C:, and rebases the source WIM path if the displaced C: volume contains that WIM. No drive-letter normalization is performed at WinPEGUI startup.

When boot configuration is requested and the successfully applied image contains a Windows directory, Imaging Manager runs BCDBoot against the restored Windows installation. Regular Apply WIM does not hard-code a system-partition drive letter; BCDBoot is allowed to use the existing firmware/system-partition configuration. If boot configuration is requested but the applied image does not contain Windows, the image apply itself succeeds and Imaging Manager reports that the BCDBoot step was skipped.

### Deploy WIM

Deploy WIM is the whole-disk counterpart to Apply WIM. It is intended for a blank/replacement disk or a deployment where the existing partition layout does not need to be preserved.

Deploy WIM detects whether WinPE was booted in UEFI or BIOS mode and creates a matching target-disk layout.

For UEFI systems, the deployment workflow creates a GPT layout containing:

- EFI System partition.
- Microsoft Reserved (MSR) partition.
- Windows partition.
- Windows Recovery partition.

For BIOS systems, the deployment workflow creates an MBR layout containing:

- Active System partition.
- Windows partition.
- Windows Recovery partition.

The Deploy confirmation dialog includes **Assign the deployed Windows partition to C: in WinPE**. It remains enabled by default to preserve the established deployment behavior. If cleared, Imaging Manager reserves an unused temporary drive letter for the new Windows partition and carries that letter consistently through DISM, BCDBoot, and Windows RE configuration; existing C: is left untouched.

Deploy WIM then:

1. Cleans/repartitions the selected physical disk.
2. Applies the selected WIM image to the new Windows partition using the selected C: or temporary WinPE access letter.
3. Requires the applied image to contain a Windows directory at that deployment access path.
4. Configures boot files with BCDBoot against the newly created System partition.
5. If `winre.wim` is present in the applied Windows image, copies it to the new Recovery partition and registers it with REAgentC.
6. Hides the Recovery partition and verifies the Windows RE configuration where possible.

Because Deploy WIM is disk-scoped, all existing partitions and data on the target disk are disposable.

### Export WIM

Export WIM is a file-to-file operation and does not require a disk or partition selection.

Imaging Manager reads the source WIM indexes, lets the desired image be selected, then exports it to a separate WIM using DISM `/Export-Image /Compress:max /CheckIntegrity`.

An existing destination requires explicit replacement confirmation. The source and destination cannot be the same file. A failed or canceled export removes the partial destination WIM rather than leaving an incomplete image behind.

### Mount WIM

Mount WIM is a file-based operation and does not require a disk or partition selection.

Imaging Manager:

1. Selects a source WIM.
2. Reads its image indexes and allows the desired image to be selected.
3. Selects an existing mount folder through `ExplorerPicker.exe`.
4. Requires the mount directory to be empty.
5. Mounts the image read/write with DISM `/Mount-Image` and `/CheckIntegrity`.

Once DISM begins the mount, the operation is intentionally not exposed as cancellable so Imaging Manager does not deliberately terminate DISM while a mount is being registered.

### Unmount WIM

The mounted-WIM strip is populated from DISM's current mounted-image inventory, so it can recognize WIMs mounted before Imaging Manager was started or mounted by another process. Imaging Manager also retains DISM's mounted-image **Status** value and uses it to expose the appropriate contextual recovery action. No second WIM-selection dialog is used.

Healthy mounted WIMs show the normal **Unmount WIM** and **Add Drivers** actions. A read-only healthy mount keeps **Add Drivers** visible but disabled. If DISM reports **Needs Remount**, the normal servicing actions are replaced with **Remount WIM**, which runs DISM `/Remount-Image /MountDir:<mount-directory>`. If DISM reports **Invalid**, the selected tile retains only **Get Info** while the global top strip exposes **Cleanup Mounts**. Abnormal statuses are appended to the mounted-WIM tile so they are visible without opening Get Info.

The unmount dialog offers:

- **Commit** — save pending changes to the WIM first, then release the mount. Imaging Manager performs this as two separately verified DISM stages: `/Commit-Image /MountDir:<mount-directory> /CheckIntegrity`, followed only after a successful commit by `/Unmount-Image /MountDir:<mount-directory> /Discard`.
- **Discard** — unmount without preserving pending changes.
- **Cancel**.

Separating commit from unmount prevents an open file or folder from making the save result ambiguous. Once `/Commit-Image` succeeds, Imaging Manager records the mount as **Committed — pending unmount** before attempting to release it. If the release fails, the WIM is already saved, the mounted-WIM tile exposes **Finish Unmount** instead of another Commit path, and retrying performs only the unmount/discard stage. The pending state is retained in the WinPE temporary directory so closing and reopening Imaging Manager in the same PE session does not accidentally offer another commit. It is reconciled against DISM's mounted-image inventory on startup and refresh.

Imaging Manager also recognizes DISM error `0xc142011d` from an older or externally initiated partial unmount/commit attempt. Because DISM indicates that a previous commit may already have succeeded, Imaging Manager warns against committing again and offers an explicit unmount-only recovery path rather than automatically repeating the commit.

Read-only mounts can be discarded but cannot be committed.

### Mounted-WIM recovery

A mounted WIM reported by DISM as **Needs Remount** can be recovered with **Remount WIM**. This uses DISM `/Remount-Image` against the selected mount directory and refreshes the mounted-image inventory afterward.

**Cleanup Mounts** remains visible in the global top strip and is enabled when DISM reports one or more mounted WIMs as **Invalid**. DISM `/Cleanup-Mountpoints` is system-wide rather than scoped to a single selected mount: it removes resources associated with corrupted mounted images, while leaving healthy mounts in place and not deleting mounts that DISM considers recoverable with `/Remount-Image`. Imaging Manager therefore refreshes the mounted-image inventory before running cleanup, requires an explicit warning/confirmation, and refreshes the inventory again afterward.

### Add Drivers

Add Drivers can service either of two offline Windows targets:

- A selected mounted WIM that is mounted read/write.
- A selected accessible partition that contains a recognizable offline Windows installation (`Windows\System32\Config\SYSTEM`).

Imaging Manager selects a driver folder and services the selected offline image with DISM `/Image:<offline-root> /Add-Driver /Driver:<folder> /Recurse`, recursively adding supported INF driver packages.

Imaging Manager does not use `/ForceUnsigned`. Unsigned packages therefore remain subject to DISM's normal validation behavior.

For a mounted WIM, driver changes remain pending until **Commit** saves them to the WIM; the subsequent unmount stage only releases the already-saved mount. Choosing **Discard** without first committing removes pending changes. For an offline installed Windows partition, DISM services that installation directly and there is no separate WIM commit step.

### Get Info, Refresh, and mounted-image state

**Get Info** opens the detailed disk, partition, or mounted-WIM information for the current selection. This keeps the main window focused on selection and operations instead of permanently displaying verbose status text.

For disks and partitions, the details window supplements the existing Win32/WMI inventory with the native `MSFT_Disk` and `MSFT_Partition` storage-provider data used by the Windows Storage stack. Disk details include identity, model/firmware, size/allocation, partition style, bus/provisioning type, operational/health state, online/read-only/system/boot flags, sector sizes, free extent, unique ID/GUID/signature, and location where available. Partition details include disk/partition identity, drive/access paths, size/offset, GPT/MBR type, operational/transition state, read-only/offline/system/boot/active/hidden attributes, volume capacity/filesystem information for accessible lettered volumes, and BitLocker details. The application queries these providers directly and does not require PowerShell; if the Storage WMI provider is unavailable in a minimal PE image, the dialog falls back to the information available from the existing Win32 inventory.

**Refresh** re-queries both the physical-disk inventory and DISM's mounted-WIM inventory. Physical-disk inventory refresh is asynchronous and coalesced, and Imaging Manager also listens for storage-topology and BitLocker-state changes so inserted/removed drives and lock-state changes can be reflected without a manual refresh. Imaging/servicing operations remain intentionally serialized: only one operation is started at a time, while the live disk inventory may continue to refresh in the background.

### Imaging safety checks

Current imaging safeguards include:

- FFU capture cannot be written to the same physical disk being captured.
- FFU apply cannot consume an FFU stored on the target disk.
- FFU apply is blocked when Imaging Manager itself is running from the target disk.
- WIM capture cannot be saved onto the partition being captured.
- WIM apply prevents using a source WIM stored on the selected target partition.
- Apply WIM verifies that its destructive target format actually occurred before starting DISM.
- Apply WIM configures boot files only when the user-selected BCDBoot option is enabled.
- Apply/Deploy WIM change the target to C: only when their confirmation-dialog option requests it; WinPEGUI does not globally normalize drive letters.
- Deploy WIM requires explicit whole-disk targeting and recreates the target partition layout.
- Export WIM prevents using the same file as both source and destination and removes incomplete output after failure/cancellation.
- Imaging Manager avoids deleting an existing WIM if an append operation fails or is canceled.
- Temporary source/target/Recovery drive-letter cleanup failures are surfaced to the user.
- Temporarily staged WinRE files are removed after WIM capture when Imaging Manager added them.
- Mounted-WIM servicing does not force unsigned drivers.

Imaging operations are destructive by nature. Verify the selected physical disk, partition, source image, destination, and commit/discard choice before starting an operation.

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

The file-manager host, taskbar host, BitLocker Manager, and Imaging Manager locate companion executables relative to their own application directory. In particular, Imaging Manager expects `ExplorerPicker.exe` for its file/folder dialogs and `BitLocker.Unlock.exe` for its integrated Unlock action.

When `Imaging.Manager.exe` is deployed alongside `Shell.Taskbar.Host.exe`, the shell exposes the Imaging Manager Start-menu entry automatically; no separate integration patch or menu configuration is required.

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

Companion applications launched by the shell, including Imaging Manager, follow the active shell theme where supported.

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
- Shutdown/reboot mounted-WIM guard, including Open Imaging Manager, Continue Anyway, and failed-inventory warning paths.
- Operation with the intended `WinPE-WMI` and `WinPE-SecureStartup` components.
- Behavior when required optional components are absent.
- BitLocker status and management.
- Passphrase unlock.
- Recovery-password unlock.
- Recovery-key-file unlock.
- BitLocker volume locking.
- Imaging Manager presence/absence in the Start menu based on whether `Imaging.Manager.exe` is deployed.
- Imaging Manager physical-disk and partition enumeration.
- Disk Management-style multi-disk rows, online/offline disk status, proportional partition sizing, partition used-space display, compact disk/partition icons, and four-disk-plus-mounted-WIM default-height layout.
- Disk, partition, and mounted-WIM selection exclusivity plus Get Info behavior.
- Full-width global command strip with Refresh pinned right and selection-driven contextual command strip.
- FFU capture to a different physical disk.
- FFU apply and destructive-target confirmation.
- FFU capture warnings for encrypted, partially encrypted, and BitLocker-status-unknown disks.
- WIM capture of normal data partitions.
- WIM capture of hidden/mountable partitions using temporary drive letters.
- Windows-partition WIM capture with `winre.wim` already present.
- Windows-partition WIM capture with WinRE temporarily staged from the configured Recovery partition.
- WinRE staging failure and cleanup-warning paths.
- New Capture WIM `/Compress:max` behavior.
- WIM replace and append-to-existing-WIM behavior.
- Multi-index WIM enumeration and image selection.
- Imaging Manager live refresh after drive insertion/removal and external BitLocker lock-state changes, including refreshes while an imaging operation is active.
- Apply WIM target quick-format and marker verification.
- Apply WIM with **Assign target to C:** both cleared and selected, including a case where C: is already occupied by another volume.
- Apply WIM to an existing Windows partition with BCDBoot defaulted on.
- Apply WIM to blank/data/test partitions with BCDBoot defaulted off.
- Apply WIM with the BCDBoot option manually enabled and disabled.
- Apply WIM failure paths where DISM succeeds but boot configuration fails or is skipped.
- Deploy WIM on UEFI/GPT systems.
- Deploy WIM with the C: assignment option enabled (default) and cleared (automatic temporary Windows letter).
- Deploy WIM on BIOS/MBR systems where supported by the target hardware.
- Deploy WIM boot configuration and Windows RE population/registration.
- Export WIM, including multi-index selection, replacement confirmation, and cancellation cleanup.
- Mount WIM to an empty folder.
- Recognition of WIMs mounted outside Imaging Manager and mounted-WIM strip selection.
- Mounted-WIM abnormal-status display, `Needs Remount` recovery, and invalid-mount cleanup behavior.
- Unmount WIM with separate Commit-Image and release stages, including an open-handle failure after a successful commit.
- **Committed — pending unmount** persistence/recovery and the **Finish Unmount** path after closing/reopening Imaging Manager in the same PE session.
- Detection/recovery guidance for DISM `0xc142011d` partial-unmount commit errors.
- Unmount WIM with Discard.
- Read-only mounted-WIM handling.
- Add Drivers against a selected writable mounted WIM, including recursive INF discovery and read-only disabled-state behavior.
- Add Drivers directly against a selected offline installed-Windows partition.
- Mounted-image refresh behavior.
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

Copyright © 2026 Dan Michel. https://github.com/IndustrialCodeGuy/WinpeGUI

WinPE GUI Shell is source-available under the PolyForm Noncommercial License 1.0.0.

Noncommercial use is permitted under the license. Commercial or other for-profit use requires separate permission from the copyright holder.

See [LICENSE.md](LICENSE.md) for the complete terms.
