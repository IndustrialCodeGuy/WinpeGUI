using BitLocker.Core;
using Imaging.Core;
using Shared.Shell.Interop;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using System.Text;

namespace Imaging.Manager;

public partial class MainForm
{
    private void SelectDiskTile(Panel tile, bool updateUi = true)
    {
        if (_selectedDiskTile == tile &&
            _selectedPartitionTile == null &&
            _selectedOpticalVolumeTile == null &&
            _selectedMountedWimTile == null)
        {
            return;
        }

        ClearOpticalVolumeSelection();
        ClearMountedWimSelection();
        ClearDiskAndPartitionSelection();
        _selectedDiskTile = tile;
        _selectionExplicitlyCleared = false;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        if (updateUi)
            UpdateSelectedDiskPanel();
    }

    private void SelectPartitionTile(Panel tile, bool updateUi = true)
    {
        if (tile.Tag is not PartitionTileContext)
            return;

        if (_selectedPartitionTile == tile &&
            _selectedOpticalVolumeTile == null &&
            _selectedMountedWimTile == null)
        {
            return;
        }

        ClearOpticalVolumeSelection();
        ClearMountedWimSelection();
        ClearDiskAndPartitionSelection();
        _selectedPartitionTile = tile;
        _selectionExplicitlyCleared = false;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        if (updateUi)
            UpdateSelectedDiskPanel();
    }

    private void SelectOpticalVolumeTile(Panel tile, bool updateUi = true)
    {
        if (tile.Tag is not ImagingVolumeInfo)
            return;

        if (_selectedOpticalVolumeTile == tile)
            return;

        ClearDiskAndPartitionSelection();
        ClearMountedWimSelection();
        ClearOpticalVolumeSelection();
        _selectedOpticalVolumeTile = tile;
        _selectionExplicitlyCleared = false;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        if (updateUi)
            UpdateSelectedDiskPanel();
    }

    private void SelectMountedWimTile(Panel tile, bool updateUi = true)
    {
        if (_selectedMountedWimTile == tile)
            return;

        ClearDiskAndPartitionSelection();
        ClearOpticalVolumeSelection();
        ClearMountedWimSelection();
        _selectedMountedWimTile = tile;
        _selectionExplicitlyCleared = false;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        if (updateUi)
            UpdateSelectedDiskPanel();
    }

    private void ClearSelectionVisuals()
    {
        ClearDiskAndPartitionSelection();
        ClearOpticalVolumeSelection();
        ClearMountedWimSelection();
    }

    private bool HasTileSelection() =>
        _selectedDiskTile != null ||
        _selectedPartitionTile != null ||
        _selectedOpticalVolumeTile != null ||
        _selectedMountedWimTile != null;

    private void DeselectTilesFromNeutralInteraction()
    {
        if (!HasTileSelection())
        {
            _selectionExplicitlyCleared = true;
            return;
        }

        ClearSelectionVisuals();
        _selectionExplicitlyCleared = true;
        UpdateSelectedDiskPanel();
    }

    private void ClearDiskAndPartitionSelection()
    {
        if (_selectedPartitionTile != null && !_selectedPartitionTile.IsDisposed)
            _selectedPartitionTile.BackColor = ShellTheme.ContentBack;
        if (_selectedDiskTile != null && !_selectedDiskTile.IsDisposed)
            _selectedDiskTile.BackColor = ShellTheme.ContentBack;

        _selectedPartitionTile = null;
        _selectedDiskTile = null;
    }

    private void ClearOpticalVolumeSelection()
    {
        if (_selectedOpticalVolumeTile != null && !_selectedOpticalVolumeTile.IsDisposed)
            _selectedOpticalVolumeTile.BackColor = ShellTheme.ContentBack;
        _selectedOpticalVolumeTile = null;
    }

    private void ClearMountedWimSelection()
    {
        if (_selectedMountedWimTile != null && !_selectedMountedWimTile.IsDisposed)
            _selectedMountedWimTile.BackColor = ShellTheme.ContentBack;
        _selectedMountedWimTile = null;
    }

    private ImagingDiskInfo? GetSelectedDisk()
    {
        if (_selectedDiskTile?.Tag is ImagingDiskInfo disk)
            return disk;

        return (_selectedPartitionTile?.Tag as PartitionTileContext)?.Disk;
    }

    private ImagingPartitionInfo? GetSelectedPartition() =>
        (_selectedPartitionTile?.Tag as PartitionTileContext)?.Partition;

    private ImagingVolumeInfo? GetSelectedOpticalVolume() =>
        _selectedOpticalVolumeTile?.Tag as ImagingVolumeInfo;

    private WimMountedImageInfo? GetSelectedMountedWim() =>
        _selectedMountedWimTile?.Tag as WimMountedImageInfo;

    private void SelectPartitionByNumber(int partitionNumber)
    {
        ImagingDiskInfo? selectedDisk = GetSelectedDisk();
        if (selectedDisk == null)
            return;

        foreach (Panel row in _pnlDisks.Controls.OfType<Panel>())
        {
            if (row.Tag is not DiskRowContext context || context.Disk.DiskNumber != selectedDisk.DiskNumber)
                continue;

            Panel? tile = context.PartitionStrip.Controls.OfType<Panel>()
                .FirstOrDefault(candidate => candidate.Tag is PartitionTileContext partitionContext &&
                                             partitionContext.Partition.PartitionNumber == partitionNumber);
            if (tile != null)
                SelectPartitionTile(tile);
            return;
        }
    }

    private static bool TryGetPartitionCaptureRoot(ImagingPartitionInfo partition, out string root)
    {
        ImagingVolumeInfo? volume = partition.Volumes.FirstOrDefault(static item =>
            item.IsReady &&
            item.MountPoint.Length > 0 &&
            item.DriveType is not DriveType.CDRom and not DriveType.Network);
        if (volume != null)
        {
            root = volume.MountPoint;
            return true;
        }

        root = string.Empty;
        return false;
    }

    private static string GetPartitionDisplayName(ImagingPartitionInfo partition)
    {
        string driveSuffix = partition.DriveLetters.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')))})";
        return $"Partition {partition.PartitionNumber}{driveSuffix}";
    }

    private static string GetPartitionTotalLine(ImagingPartitionInfo partition) =>
        $"Total: {FormatBytes(partition.SizeBytes)}";

    private static string GetPartitionUsedLine(ImagingPartitionInfo partition)
    {
        ImagingVolumeInfo? volume = partition.Volumes.FirstOrDefault(static item =>
            item.IsReady && item.TotalSizeBytes > 0);
        return volume == null
            ? "Used: —"
            : $"Used: {FormatBytes(volume.UsedSpaceBytes)}";
    }

    private static bool IsMountedWimStatus(WimMountedImageInfo image, string status) =>
        string.Equals(image.Status?.Trim(), status, StringComparison.OrdinalIgnoreCase);

    private static bool IsMountedWimHealthyOrUnknown(WimMountedImageInfo image) =>
        string.IsNullOrWhiteSpace(image.Status) || IsMountedWimStatus(image, "OK");

    private string GetMountedWimAbnormalStatus(WimMountedImageInfo image)
    {
        if (IsPendingWimUnmount(image))
            return "Committed — pending unmount";
        if (IsMountedWimStatus(image, "Needs Remount"))
            return "Needs Remount";
        if (IsMountedWimStatus(image, "Invalid"))
            return "Invalid";
        if (!IsMountedWimHealthyOrUnknown(image))
            return image.Status.Trim();
        return string.Empty;
    }

    private string GetDiskStatusText(ImagingDiskInfo disk)
    {
        if (_operationCoordinator.TryGetDiskOperationName(disk, out string operationName))
            return operationName + "...";

        return disk.IsOffline switch
        {
            true => "Offline",
            false => "Online",
            _ => "Status unknown"
        };
    }

    private void RefreshDiskOperationIndicators()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return;

        foreach (Panel row in _pnlDisks.Controls.OfType<Panel>())
        {
            if (row.Tag is not DiskRowContext context)
                continue;

            Label? status = context.DiskTile.Controls["DiskStatusLabel"] as Label;
            if (status != null)
                status.Text = GetDiskStatusText(context.Disk);
        }

        if (!IsDisposed && !Disposing)
            UpdateSelectedDiskPanel();
    }

    private void UpdateSelectedDiskPanel()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        ImagingVolumeInfo? opticalVolume = GetSelectedOpticalVolume();
        WimMountedImageInfo? mountedWim = GetSelectedMountedWim();

        bool diskSelectionActive = disk != null && partition == null && opticalVolume == null && mountedWim == null;
        bool partitionSelectionActive = disk != null && partition != null && opticalVolume == null && mountedWim == null;
        bool opticalSelectionActive = opticalVolume != null;
        bool mountedWimSelectionActive = mountedWim != null;
        bool mountedWimPendingUnmount = mountedWimSelectionActive &&
                                        IsPendingWimUnmount(mountedWim!);
        bool mountedWimNeedsRemount = mountedWimSelectionActive &&
                                      !mountedWimPendingUnmount &&
                                      IsMountedWimStatus(mountedWim!, "Needs Remount");
        bool anyInvalidMountedWim = _mountedWims.Any(static image =>
            IsMountedWimStatus(image, "Invalid"));
        bool mountedWimHealthyOrUnknown = mountedWimSelectionActive &&
                                          !mountedWimPendingUnmount &&
                                          IsMountedWimHealthyOrUnknown(mountedWim!);
        bool partitionIsOfflineWindows = partitionSelectionActive &&
                                         TryGetOfflineWindowsRoot(partition!, out _);
        bool partitionIsLocked = partitionSelectionActive &&
                                 GetBitLockerVolumeForPartition(partition!)?.IsLocked == true;
        string selectedDiskOperationName = string.Empty;
        bool selectedDiskBusy = disk != null &&
                                _operationCoordinator.TryGetDiskOperationName(disk, out selectedDiskOperationName);

        _lblStatus.ForeColor = GetInformationTextColor();
        bool actionsAvailable = !_initialInventoryLoading && !_operationActive && !_diskRefreshInProgress;

        _miMountWim.Enabled = actionsAvailable;
        _miExportWim.Enabled = actionsAvailable;
        _miImageInfo.Enabled = actionsAvailable;
        _miDeleteWimImage.Enabled = actionsAvailable;
        _miSplitWim.Enabled = actionsAvailable;
        _miCleanupMounts.Enabled = actionsAvailable && anyInvalidMountedWim;
        _miRefresh.Enabled = actionsAvailable;

        _actionGetInfo.Visible = diskSelectionActive || partitionSelectionActive || opticalSelectionActive || mountedWimSelectionActive;
        _actionCaptureFfu.Visible = diskSelectionActive;
        _actionApplyFfu.Visible = diskSelectionActive;
        _actionDeployWim.Visible = diskSelectionActive;
        _actionCaptureWim.Visible = partitionSelectionActive;
        _actionApplyWim.Visible = partitionSelectionActive;
        _actionUnmountWim.Text = mountedWimPendingUnmount ? "Finish Unmount" : "Unmount WIM";
        _actionUnmountWim.Visible = mountedWimHealthyOrUnknown || mountedWimPendingUnmount;
        _actionRemountWim.Visible = mountedWimNeedsRemount;
        _actionAddDrivers.Visible = mountedWimHealthyOrUnknown || partitionIsOfflineWindows;
        _actionUnlock.Visible = partitionIsLocked;

        _actionGetInfo.Enabled = actionsAvailable && _actionGetInfo.Visible;
        _actionCaptureFfu.Enabled = actionsAvailable && diskSelectionActive;
        _actionApplyFfu.Enabled = actionsAvailable && diskSelectionActive;
        _actionDeployWim.Enabled = actionsAvailable && diskSelectionActive;
        _actionCaptureWim.Enabled = actionsAvailable && partitionSelectionActive;
        _actionApplyWim.Enabled = actionsAvailable && partitionSelectionActive;
        _actionUnmountWim.Enabled = actionsAvailable && (mountedWimHealthyOrUnknown || mountedWimPendingUnmount);
        _actionRemountWim.Enabled = actionsAvailable && mountedWimNeedsRemount;
        _actionAddDrivers.Enabled = actionsAvailable &&
                                 ((mountedWimHealthyOrUnknown && mountedWim!.ReadWrite) ||
                                  partitionIsOfflineWindows);
        _actionUnlock.Enabled = actionsAvailable && partitionIsLocked;

        string selectionBusySuffix = selectedDiskBusy
            ? $" — {selectedDiskOperationName} in progress"
            : string.Empty;

        _lblSelectionContext.Text = mountedWimSelectionActive
            ? mountedWim!.DisplayName
            : opticalSelectionActive
                ? $"{opticalVolume!.DisplayName} — CD Drive"
                : partitionSelectionActive
                    ? GetPartitionDisplayName(partition!) + selectionBusySuffix
                    : diskSelectionActive
                        ? $"Disk {disk!.DiskNumber}" + selectionBusySuffix
                        : "Select a disk, partition, optical volume, or mounted WIM";

        LayoutContextActionStrip(_mPx.DetailButtonWidth, _mPx.DetailButtonHeight, _mPx.DetailButtonGap);
        UpdateStatusLine();
    }

    private static bool TryGetOfflineWindowsRoot(ImagingPartitionInfo partition, out string root)
    {
        ImagingVolumeInfo? volume = partition.Volumes.FirstOrDefault(static item =>
            item.IsReady &&
            !item.IsRunningSystemDrive &&
            item.ContainsOfflineWindowsInstall);
        if (volume != null)
        {
            root = volume.MountPoint;
            return true;
        }

        root = string.Empty;
        return false;
    }

    private void ShowSelectedInfo()
    {
        string title;
        string details;

        if (GetSelectedMountedWim() is WimMountedImageInfo mountedWim)
        {
            title = "Mounted WIM Information";
            details = BuildMountedWimDetails(mountedWim);
        }
        else if (GetSelectedOpticalVolume() is ImagingVolumeInfo opticalVolume)
        {
            title = $"{opticalVolume.MountPoint.TrimEnd('\\')} Optical Media Information";
            details = BuildOpticalVolumeDetails(opticalVolume);
        }
        else if (GetSelectedPartition() is ImagingPartitionInfo partition)
        {
            ImagingDiskInfo? parentDisk = GetSelectedDisk();
            title = $"{GetPartitionDisplayName(partition)} Information";
            details = BuildPartitionDetails(parentDisk, partition);
        }
        else if (GetSelectedDisk() is ImagingDiskInfo disk)
        {
            title = $"Disk {disk.DiskNumber} Information";
            details = BuildDiskDetails(disk);
        }
        else
        {
            return;
        }

        using SelectionInfoDialog dialog = new(title, details);
        dialog.ShowDialog(this);
    }

    private string BuildDiskDetails(ImagingDiskInfo disk)
    {
        StringBuilder text = new();
        ImagingDiskStorageInfo? storage = disk.StorageInfo;

        AppendInfoSection(text, $"Disk {disk.DiskNumber}");
        AppendInfoLine(text, "Number", disk.DiskNumber.ToString());
        AppendInfoLine(text, "Friendly name", FirstNonEmpty(storage?.FriendlyName, disk.Model));
        AppendInfoLine(text, "Model", FirstNonEmpty(storage?.Model, disk.Model));
        AppendInfoLine(text, "Manufacturer", storage?.Manufacturer);
        AppendInfoLine(text, "Serial number", FirstNonEmpty(storage?.SerialNumber, disk.SerialNumber));
        AppendInfoLine(text, "Firmware", storage?.FirmwareVersion);
        AppendInfoLine(text, "Size", FormatBytes(storage is { SizeBytes: > 0 } ? storage.SizeBytes : disk.SizeBytes));
        AppendInfoLine(text, "Partition style", storage?.PartitionStyle);
        AppendInfoLine(text, "Bus type", storage?.BusType);
        AppendInfoLine(text, "Interface", disk.InterfaceType);
        AppendInfoLine(text, "Media type", disk.MediaType);
        AppendInfoLine(text, "Operational status", FirstNonEmpty(storage?.OperationalStatus, FormatOnlineState(disk.IsOffline)));
        AppendInfoLine(text, "Health status", storage?.HealthStatus);
        AppendInfoLine(text, "Offline", FormatNullableBoolean(storage?.IsOffline ?? disk.IsOffline));
        if (storage != null)
        {
            if (storage.IsOffline == true)
                AppendInfoLine(text, "Offline reason", storage.OfflineReason);
            AppendInfoLine(text, "Read only", FormatNullableBoolean(storage.IsReadOnly));
            AppendInfoLine(text, "System disk", FormatNullableBoolean(storage.IsSystem));
            AppendInfoLine(text, "Boot disk", FormatNullableBoolean(storage.IsBoot));
            AppendInfoLine(text, "Boot from disk", FormatNullableBoolean(storage.BootFromDisk));
            AppendInfoLine(text, "Clustered", FormatNullableBoolean(storage.IsClustered));
        }

        AppendInfoSection(text, "Capacity / geometry");
        if (storage != null)
        {
            AppendInfoLine(text, "Allocated size", FormatBytes(storage.AllocatedSizeBytes));
            AppendInfoLine(text, "Largest free extent", FormatBytes(storage.LargestFreeExtentBytes));
            AppendInfoLine(text, "Partitions", storage.NumberOfPartitions.ToString());
            AppendInfoLine(text, "Provisioning", storage.ProvisioningType);
            AppendInfoLine(text, "Logical sector size", FormatByteCount(storage.LogicalSectorSize));
            AppendInfoLine(text, "Physical sector size", FormatByteCount(storage.PhysicalSectorSize));
        }
        else
        {
            AppendInfoLine(text, "Partitions", disk.Partitions.Count.ToString());
        }

        AppendInfoSection(text, "Identity / paths");
        AppendInfoLine(text, "Device", disk.DevicePath);
        if (storage != null)
        {
            AppendInfoLine(text, "Storage path", storage.Path);
            AppendInfoLine(text, "Location", storage.Location);
            AppendInfoLine(text, "Unique ID", storage.UniqueId);
            AppendInfoLine(text, "Unique ID format", storage.UniqueIdFormat);
            if (string.Equals(storage.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
                AppendInfoLine(text, "Disk GUID", storage.Guid);
            if (string.Equals(storage.PartitionStyle, "MBR", StringComparison.OrdinalIgnoreCase) && storage.Signature.HasValue)
                AppendInfoLine(text, "MBR signature", $"0x{storage.Signature.Value:X8}");
        }

        if (!disk.StorageInfoAvailable)
        {
            AppendInfoSection(text, "Storage provider");
            text.AppendLine("Detailed MSFT_Disk information is unavailable in this environment.");
            if (!string.IsNullOrWhiteSpace(disk.StorageInfoError))
                AppendInfoLine(text, "Error", disk.StorageInfoError);
        }

        AppendBitLockerDetails(text, disk);
        return text.ToString().TrimEnd();
    }

    private string BuildPartitionDetails(ImagingDiskInfo? disk, ImagingPartitionInfo partition)
    {
        StringBuilder text = new();
        ImagingPartitionStorageInfo? storage = partition.StorageInfo;

        int reportedPartitionNumber = storage?.PartitionNumber ?? partition.PartitionNumber;
        AppendInfoSection(text, $"Partition {reportedPartitionNumber}");
        if (disk != null)
            AppendInfoLine(text, "Disk number", disk.DiskNumber.ToString());
        AppendInfoLine(text, "Partition number", reportedPartitionNumber.ToString());
        AppendInfoLine(text, "Win32 partition index", partition.Win32PartitionIndex.ToString());

        string drives = partition.DriveLetters.Count == 0
            ? "None"
            : string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')));
        AppendInfoLine(text, "Drive letter(s)", drives);
        AppendInfoLine(text, "Size", FormatBytes(storage is { SizeBytes: > 0 } ? storage.SizeBytes : partition.SizeBytes));
        AppendInfoLine(text, "Offset", FormatByteOffset(storage?.OffsetBytes ?? partition.StartingOffsetBytes));
        AppendInfoLine(text, "Win32 type", partition.Type);
        AppendInfoLine(text, "Operational status", storage?.OperationalStatus);
        AppendInfoLine(text, "Transition state", storage?.TransitionState);

        if (storage != null)
        {
            AppendInfoLine(text, "GPT type", storage.GptType);
            AppendInfoLine(text, "MBR type", storage.MbrType);
            AppendInfoLine(text, "Partition GUID", storage.Guid);
        }

        AppendInfoSection(text, "Attributes");
        AppendInfoLine(text, "Primary", partition.PrimaryPartition ? "Yes" : "No");
        AppendInfoLine(text, "Boot (Win32)", partition.BootPartition ? "Yes" : "No");
        if (storage != null)
        {
            AppendInfoLine(text, "Read only", FormatNullableBoolean(storage.IsReadOnly));
            AppendInfoLine(text, "Offline", FormatNullableBoolean(storage.IsOffline));
            AppendInfoLine(text, "System", FormatNullableBoolean(storage.IsSystem));
            AppendInfoLine(text, "Boot", FormatNullableBoolean(storage.IsBoot));
            AppendInfoLine(text, "Active", FormatNullableBoolean(storage.IsActive));
            AppendInfoLine(text, "Hidden", FormatNullableBoolean(storage.IsHidden));
            AppendInfoLine(text, "Shadow copy", FormatNullableBoolean(storage.IsShadowCopy));
            AppendInfoLine(text, "No default drive letter", FormatNullableBoolean(storage.NoDefaultDriveLetter));
        }

        AppendInfoSection(text, "Paths");
        AppendInfoLine(text, "Device", partition.DeviceId);
        if (storage != null)
        {
            AppendInfoLine(text, "Storage drive letter", storage.DriveLetter);
            if (storage.AccessPaths.Count == 0)
            {
                AppendInfoLine(text, "Access paths", "None");
            }
            else
            {
                AppendInfoLine(text, "Access paths", storage.AccessPaths[0]);
                foreach (string path in storage.AccessPaths.Skip(1))
                    AppendInfoContinuation(text, path);
            }
        }

        AppendVolumeDetails(text, partition);
        AppendPartitionBitLockerDetails(text, partition);

        if (storage == null)
        {
            AppendInfoSection(text, "Storage provider");
            text.AppendLine("Detailed MSFT_Partition information is unavailable for this partition.");
            if (disk != null && !disk.PartitionStorageInfoAvailable && !string.IsNullOrWhiteSpace(disk.PartitionStorageInfoError))
                AppendInfoLine(text, "Error", disk.PartitionStorageInfoError);
        }

        return text.ToString().TrimEnd();
    }

    private static string BuildOpticalVolumeDetails(ImagingVolumeInfo volume)
    {
        StringBuilder text = new();
        AppendInfoSection(text, "Optical Media");
        AppendInfoLine(text, "Drive", volume.MountPoint.TrimEnd('\\'));
        AppendInfoLine(text, "Drive type", "CD Drive");
        AppendInfoLine(text, "Ready", volume.IsReady ? "Yes" : "No");
        if (volume.IsReady)
        {
            AppendInfoLine(text, "Label", volume.VolumeLabel);
            AppendInfoLine(text, "File system", volume.DriveFormat);
            AppendInfoLine(text, "Total", FormatBytes(volume.TotalSizeBytes));
            AppendInfoLine(text, "Used", FormatBytes(volume.UsedSpaceBytes));
            AppendInfoLine(text, "Free", FormatBytes(volume.TotalFreeSpaceBytes));
        }

        return text.ToString().TrimEnd();
    }

    private string BuildMountedWimDetails(WimMountedImageInfo image)
    {
        StringBuilder text = new();
        AppendInfoSection(text, "Mounted WIM");
        AppendInfoLine(text, "Image", image.ImageFile);
        if (image.ImageIndex > 0)
            AppendInfoLine(text, "Index", image.ImageIndex.ToString());
        AppendInfoLine(text, "Mount", image.MountDirectory);
        AppendInfoLine(text, "Mode", image.ReadWrite ? "Read/write" : "Read-only");
        AppendInfoLine(text, "Status", string.IsNullOrWhiteSpace(image.Status) ? "Unknown" : image.Status);
        if (IsPendingWimUnmount(image))
            AppendInfoLine(text, "Pending action", "Finish unmount (changes already committed)");
        return text.ToString().TrimEnd();
    }

    private void AppendBitLockerDetails(StringBuilder text, ImagingDiskInfo disk)
    {
        AppendInfoSection(text, "BitLocker");
        if (!disk.BitLockerStatusAvailable)
        {
            text.AppendLine("BitLocker status unavailable; encryption state could not be verified.");
            if (!string.IsNullOrWhiteSpace(disk.BitLockerStatusError))
                AppendInfoLine(text, "Status error", disk.BitLockerStatusError);
            return;
        }

        if (disk.BitLockerVolumes.Count == 0)
        {
            text.AppendLine("No BitLocker-capable volume detected on a lettered partition.");
            return;
        }

        foreach (ImagingBitLockerVolumeInfo volume in disk.BitLockerVolumes)
        {
            string mount = volume.MountPoint.TrimEnd('\\');
            string state = volume.IsLocked switch { true => "Locked", false => "Unlocked", _ => "Lock unknown" };
            string conversion = string.IsNullOrWhiteSpace(volume.ConversionStatus) ? "Status unknown" : volume.ConversionStatus;
            string percent = volume.EncryptionPercentage.HasValue ? $"{volume.EncryptionPercentage.Value}%" : "Unknown";
            text.AppendLine(mount.Length == 0 ? "Volume" : mount);
            AppendInfoLine(text, "  Conversion", conversion);
            AppendInfoLine(text, "  Encrypted", percent);
            AppendInfoLine(text, "  Encryption type", volume.EncryptionType);
            AppendInfoLine(text, "  Protection", volume.ProtectionStatus);
            AppendInfoLine(text, "  Lock state", state);
        }
    }

    private void AppendPartitionBitLockerDetails(StringBuilder text, ImagingPartitionInfo partition)
    {
        ImagingBitLockerVolumeInfo? bitLocker = GetBitLockerVolumeForPartition(partition);
        if (bitLocker?.IsBitLockerCapable != true)
            return;

        AppendInfoSection(text, "BitLocker");
        string state = bitLocker.IsLocked switch
        {
            true => "Locked",
            false => bitLocker.VisualState == BitLockerVisualState.ProtectionOff ? "Unlocked · Protection off" : "Unlocked",
            _ => "Status unknown"
        };
        AppendInfoLine(text, "Status", state);
        if (bitLocker.EncryptionPercentage.HasValue)
            AppendInfoLine(text, "Encrypted", $"{bitLocker.EncryptionPercentage.Value}%");
        AppendInfoLine(text, "Conversion", bitLocker.ConversionStatus);
        AppendInfoLine(text, "Encryption type", bitLocker.EncryptionType);
        AppendInfoLine(text, "Protection", bitLocker.ProtectionStatus);
        AppendInfoLine(text, "Volume type", bitLocker.VolumeTypeText);
        AppendInfoLine(text, "Volume label", bitLocker.VolumeLabel);
    }

    private static void AppendVolumeDetails(StringBuilder text, ImagingPartitionInfo partition)
    {
        if (partition.Volumes.Count == 0)
            return;

        AppendInfoSection(text, "Volume");
        foreach (ImagingVolumeInfo volume in partition.Volumes)
        {
            text.AppendLine(volume.MountPoint.TrimEnd('\\'));
            AppendInfoLine(text, "  Ready", volume.IsReady ? "Yes" : "No");
            AppendInfoLine(text, "  Drive type", volume.DriveType.ToString());
            if (!volume.IsReady)
                continue;

            AppendInfoLine(text, "  Label", volume.VolumeLabel);
            AppendInfoLine(text, "  File system", volume.DriveFormat);
            AppendInfoLine(text, "  Total", FormatBytes(volume.TotalSizeBytes));
            AppendInfoLine(text, "  Used", FormatBytes(volume.UsedSpaceBytes));
            AppendInfoLine(text, "  Free", FormatBytes(volume.TotalFreeSpaceBytes));
            AppendInfoLine(text, "  Available free", FormatBytes(volume.AvailableFreeSpaceBytes));
        }
    }

    private static void AppendInfoSection(StringBuilder text, string heading)
    {
        if (text.Length > 0)
            text.AppendLine();
        text.AppendLine(heading);
        text.AppendLine(new string('-', Math.Min(heading.Length, 48)));
    }

    private static void AppendInfoLine(StringBuilder text, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        text.AppendLine($"{label + ":",-24}{value.Trim()}");
    }

    private static void AppendInfoContinuation(StringBuilder text, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            text.AppendLine($"{"",-24}{value.Trim()}");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FormatNullableBoolean(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        _ => "Unknown"
    };

    private static string FormatOnlineState(bool? isOffline) => isOffline switch
    {
        true => "Offline",
        false => "Online",
        _ => "Unknown"
    };

    private static string FormatByteCount(uint bytes) => bytes == 0 ? "Unknown" : $"{bytes:N0} bytes";

    private static string FormatByteOffset(ulong bytes) => $"{FormatBytes(bytes)} ({bytes:N0} bytes)";

    private void UpdateStatusLine()
    {
        bool wasVisible = _lblStatus.Visible;

        if (_initialInventoryLoading)
        {
            _lblStatus.Text = "Loading storage and mounted WIM inventory...";
            _lblStatus.Visible = true;
        }
        else if (!string.IsNullOrWhiteSpace(_loadError))
        {
            _lblStatus.Text = _loadError;
            _lblStatus.Visible = true;
        }
        else if (_disks.Count == 0 && _opticalVolumes.Count == 0 && _mountedWims.Count == 0)
        {
            _lblStatus.Text = "No physical disks, mounted optical media, or mounted WIMs found.";
            _lblStatus.Visible = true;
        }
        else
        {
            _lblStatus.Text = string.Empty;
            _lblStatus.Visible = false;
        }

        if (wasVisible != _lblStatus.Visible && _rightPanel is { IsDisposed: false })
            LayoutDiskDetails(_rightPanel);
    }

    private static string FormatBytes(ulong bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;
        const double tb = gb * 1024d;

        if (bytes >= tb) return $"{bytes / tb:0.##} TB";
        if (bytes >= gb) return $"{bytes / gb:0.##} GB";
        if (bytes >= mb) return $"{bytes / mb:0.##} MB";
        if (bytes >= kb) return $"{bytes / kb:0.##} KB";
        return $"{bytes} B";
    }
}
