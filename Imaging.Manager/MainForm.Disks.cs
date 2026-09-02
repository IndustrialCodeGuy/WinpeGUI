using BitLocker.Core;
using Imaging.Core;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using System.Text;

namespace Imaging.Manager;

public partial class MainForm
{
    private void InitializeDiskUi()
    {
        _splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = _mPx.LeftPanelWidth,
            IsSplitterFixed = true,
            SplitterWidth = _mPx.SplitterWidth,
            BackColor = ShellTheme.ContentBorder
        };

        _pnlDisks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pnlDisks.Resize += (_, _) => LayoutDiskTiles();
        _splitMain.Panel1.Controls.Add(_pnlDisks);

        _rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.WindowBack
        };

        _pnlPartitions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pnlPartitions.Resize += (_, _) => LayoutPartitionTiles();

        _txtDiskStatus = new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            // In full Windows SystemColors.WindowText is normally black, but
            // some WinPE light themes expose a muted value. Keep this body text
            // dark in light mode while retaining the shared dark-mode color.
            ForeColor = GetInformationTextColor(),
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
            Text = "No disk selected"
        };

        _btnCapture = new Button
        {
            Text = "Capture FFU",
            UseVisualStyleBackColor = true
        };
        _btnCapture.Click += async (_, _) => await CaptureSelectedDiskAsync();

        _btnApply = new Button
        {
            Text = "Apply FFU",
            UseVisualStyleBackColor = true
        };
        _btnApply.Click += async (_, _) => await ApplyToSelectedDiskAsync();

        _btnRefresh = new Button
        {
            Text = "Refresh",
            UseVisualStyleBackColor = true
        };
        _btnRefresh.Click += async (_, _) => await RefreshViewAsync();

        _btnMountWim = new Button
        {
            Text = "Mount WIM",
            UseVisualStyleBackColor = true
        };
        _btnMountWim.Click += async (_, _) => await MountWimAsync();

        _btnUnmountWim = new Button
        {
            Text = "Unmount WIM",
            UseVisualStyleBackColor = true
        };
        _btnUnmountWim.Click += async (_, _) => await UnmountWimAsync();

        _btnCaptureWim = new Button
        {
            Text = "Capture WIM",
            UseVisualStyleBackColor = true
        };
        _btnCaptureWim.Click += async (_, _) => await CaptureSelectedPartitionWimAsync();

        _btnApplyWim = new Button
        {
            Text = "Apply WIM",
            UseVisualStyleBackColor = true
        };
        _btnApplyWim.Click += async (_, _) => await ApplyWimToSelectedPartitionAsync();

        _btnUnlock = new Button
        {
            Text = "Unlock",
            UseVisualStyleBackColor = true
        };
        _btnUnlock.Click += async (_, _) => await UnlockSelectedPartitionAsync();

        _btnDeployWim = new Button
        {
            Text = "Deploy WIM",
            UseVisualStyleBackColor = true
        };
        _btnDeployWim.Click += async (_, _) => await DeployWimToSelectedDiskAsync();

        _btnExportWim = new Button
        {
            Text = "Export WIM",
            UseVisualStyleBackColor = true
        };
        _btnExportWim.Click += async (_, _) => await ExportWimAsync();

        _btnAddDrivers = new Button
        {
            Text = "Add Drivers",
            UseVisualStyleBackColor = true
        };
        _btnAddDrivers.Click += async (_, _) => await AddDriversAsync();

        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = GetInformationTextColor(),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _rightPanel.Controls.Add(_pnlPartitions);
        _rightPanel.Controls.Add(_txtDiskStatus);
        _rightPanel.Controls.Add(_btnCapture);
        _rightPanel.Controls.Add(_btnApply);
        _rightPanel.Controls.Add(_btnRefresh);
        _rightPanel.Controls.Add(_btnMountWim);
        _rightPanel.Controls.Add(_btnUnmountWim);
        _rightPanel.Controls.Add(_btnCaptureWim);
        _rightPanel.Controls.Add(_btnApplyWim);
        _rightPanel.Controls.Add(_btnUnlock);
        _rightPanel.Controls.Add(_btnDeployWim);
        _rightPanel.Controls.Add(_btnExportWim);
        _rightPanel.Controls.Add(_btnAddDrivers);
        _rightPanel.Controls.Add(_lblStatus);
        _rightPanel.Resize += (_, _) => LayoutDiskDetails(_rightPanel);

        _splitMain.Panel2.Controls.Add(_rightPanel);
        Controls.Add(_splitMain);

        ApplyChromeFonts();
        LayoutDiskDetails(_rightPanel);
    }

    private static Color GetInformationTextColor() =>
        ShellTheme.DarkMode ? ShellTheme.TextColor : Color.Black;

    private void LayoutDiskDetails(Control panel)
    {
        if (panel.ClientSize.Width <= 0 || panel.ClientSize.Height <= 0)
            return;

        int margin = _mPx.DetailMargin;
        int gap = _mPx.DetailGap;
        int buttonGap = _mPx.DetailButtonGap;
        int buttonWidth = _mPx.DetailButtonWidth;
        int buttonHeight = _mPx.DetailButtonHeight;
        int statusHeight = _mPx.DetailStatusHeight;

        // Keep the partition selector and information box in one aligned content
        // column. The action column is fixed-width on the right.
        int buttonLeft = Math.Max(margin, panel.ClientSize.Width - margin - buttonWidth);
        // Use the same outer margin on both sides of the content column.
        int contentWidth = Math.Max(0, buttonLeft - margin - margin);
        int contentLeft = margin;

        // Let the partition selector start at the top edge of the right pane.
        // The aligned content column keeps its normal left/right margin while
        // reclaiming the former top margin for the information box below.
        int partitionTop = 0;
        int detailsTop = _mPx.PartitionPaneHeight + gap;

        // Normally let the information box run all the way to the standard
        // bottom margin. When a status message is visible, reserve one normal
        // gap for the status line instead of permanently giving that space up.
        int contentBottom = Math.Max(detailsTop, panel.ClientSize.Height - margin);
        int statusTop = contentBottom;
        int detailsBottom = contentBottom;
        if (_lblStatus.Visible)
        {
            statusTop = Math.Max(detailsTop, contentBottom - statusHeight);
            detailsBottom = Math.Max(detailsTop, statusTop - gap);
        }

        int detailsHeight = Math.Max(0, detailsBottom - detailsTop);

        SetBoundsIfChanged(_pnlPartitions, contentLeft, partitionTop, contentWidth, _mPx.PartitionPaneHeight);
        SetBoundsIfChanged(_txtDiskStatus, contentLeft, detailsTop, contentWidth, detailsHeight);
        SetBoundsIfChanged(_lblStatus, contentLeft, statusTop, contentWidth, statusHeight);

        int buttonTop = margin;
        SetBoundsIfChanged(_btnCapture, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnApply, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnMountWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnUnmountWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnCaptureWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnApplyWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnDeployWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnExportWim, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnAddDrivers, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnUnlock, buttonLeft, buttonTop, buttonWidth, buttonHeight);
        buttonTop += buttonHeight + buttonGap;
        SetBoundsIfChanged(_btnRefresh, buttonLeft, buttonTop, buttonWidth, buttonHeight);
    }

    private void LoadDisks(int? selectDiskNumber = null)
    {
        if (_isLoading || _operationActive)
            return;

        int? selectedPartitionNumber = GetSelectedPartition()?.PartitionNumber;

        _isLoading = true;
        UseWaitCursor = true;
        _loadError = string.Empty;
        try
        {
            _disks = _inventory.GetDisks();
        }
        catch (Exception ex)
        {
            _disks = Array.Empty<ImagingDiskInfo>();
            _loadError = ex.Message;
        }
        finally
        {
            _isLoading = false;
            UseWaitCursor = false;
        }

        RebuildDiskTiles(selectDiskNumber, selectedPartitionNumber);
        ApplyLayoutMetrics();
        UpdateSelectedDiskPanel();
    }

    private void RebuildDiskTiles(int? selectDiskNumber, int? preferredPartitionNumber)
    {
        _pnlDisks.SuspendLayout();
        try
        {
            while (_pnlDisks.Controls.Count > 0)
            {
                Control control = _pnlDisks.Controls[0];
                _pnlDisks.Controls.RemoveAt(0);
                control.Dispose();
            }

            _selectedDiskTile = null;
            ClearPartitionTiles();

            foreach (ImagingDiskInfo disk in _disks)
            {
                Panel tile = CreateDiskTile(disk);
                _pnlDisks.Controls.Add(tile);

                if (selectDiskNumber == disk.DiskNumber)
                    SelectDiskTile(tile, preferredPartitionNumber);
            }

            if (_selectedDiskTile == null && _pnlDisks.Controls.OfType<Panel>().FirstOrDefault() is Panel first)
                SelectDiskTile(first);
        }
        finally
        {
            _pnlDisks.ResumeLayout(true);
        }
    }

    private Panel CreateDiskTile(ImagingDiskInfo disk)
    {
        Panel tile = new()
        {
            Width = GetDiskTileWidth(),
            Height = _mPx.DiskTileHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = disk
        };

        PictureBox picture = new()
        {
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = GetDiskImage(),
            Cursor = Cursors.Hand
        };

        Label name = new()
        {
            Text = $"Disk {disk.DiskNumber}",
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label sub = new()
        {
            Text = FormatBytes(disk.SizeBytes),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void select(object? _, EventArgs __) => SelectDiskTile(tile);
        tile.Click += select;
        picture.Click += select;
        name.Click += select;
        sub.Click += select;

        void hover(object? _, EventArgs __)
        {
            if (_selectedDiskTile != tile)
                tile.BackColor = ShellTheme.ItemHoverBack;
        }

        void leave(object? _, EventArgs __)
        {
            if (tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
                return;
            if (_selectedDiskTile != tile)
                tile.BackColor = ShellTheme.ContentBack;
        }

        tile.MouseEnter += hover;
        picture.MouseEnter += hover;
        name.MouseEnter += hover;
        sub.MouseEnter += hover;
        tile.MouseLeave += leave;
        picture.MouseLeave += leave;
        name.MouseLeave += leave;
        sub.MouseLeave += leave;

        tile.Controls.Add(picture);
        tile.Controls.Add(name);
        tile.Controls.Add(sub);
        LayoutDiskTile(tile);
        return tile;
    }

    private void LayoutDiskTiles()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return;

        int width = GetDiskTileWidth();
        foreach (Panel tile in _pnlDisks.Controls.OfType<Panel>())
        {
            tile.Width = width;
            tile.Height = _mPx.DiskTileHeight;
            LayoutDiskTile(tile);
        }
    }

    private void LayoutDiskTile(Panel tile)
    {
        PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
        Label[] labels = tile.Controls.OfType<Label>().ToArray();
        if (picture != null)
            SetBoundsIfChanged(picture, (tile.Width - _mPx.DiskTileIconSize) / 2, _mPx.DiskTileIconTop, _mPx.DiskTileIconSize, _mPx.DiskTileIconSize);
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], _mPx.DiskTilePadX, _mPx.DiskTileNameTop, Math.Max(0, tile.Width - (_mPx.DiskTilePadX * 2)), _mPx.DiskTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], _mPx.DiskTilePadX, _mPx.DiskTileSubTop, Math.Max(0, tile.Width - (_mPx.DiskTilePadX * 2)), _mPx.DiskTileSubHeight);
    }

    private int GetDiskTileWidth()
    {
        int width = _pnlDisks.ClientSize.Width - _pnlDisks.Padding.Left - _pnlDisks.Padding.Right;
        if (_pnlDisks.VerticalScroll.Visible)
            width -= SystemInformation.VerticalScrollBarWidth;
        return Math.Max(_mPx.DiskPaneMinimumWidth - _mPx.DiskPaneBorderAllowance, width);
    }

    private int GetDesiredDiskPaneWidth()
    {
        int desired = _mPx.LeftPanelWidth;
        Font font = _chromeFont ?? Font;

        foreach (ImagingDiskInfo disk in _disks)
        {
            // The tile renders these on separate lines. Measuring the old
            // combined "Disk N  size" string made this pane wider than the
            // equivalent BitLocker selector for no visual benefit.
            int nameWidth = TextRenderer.MeasureText(
                $"Disk {disk.DiskNumber}",
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int sizeWidth = TextRenderer.MeasureText(
                FormatBytes(disk.SizeBytes),
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

            desired = Math.Max(
                desired,
                Math.Max(nameWidth, sizeWidth) + (_mPx.DiskTilePadX * 2) + _mPx.DiskPaneBorderAllowance);
        }

        if (_pnlDisks is { IsDisposed: false } && _disks.Count * _mPx.DiskTileHeight > _pnlDisks.ClientSize.Height)
            desired += _mPx.DiskPaneScrollBarAllowance;

        return Math.Clamp(desired, _mPx.DiskPaneMinimumWidth, _mPx.DiskPaneMaximumWidth);
    }

    private Image GetDiskImage()
    {
        int size = _mPx.DiskTileIconSize;
        if (_diskImagesBySize.TryGetValue(size, out Image? image))
            return image;

        string imageresPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        image = IconUtil.FromFileIconIndex(imageresPath, 30, size);
        if (image == null)
        {
            using Bitmap fallback = SystemIcons.WinLogo.ToBitmap();
            image = new Bitmap(fallback, new Size(size, size));
        }

        _diskImagesBySize[size] = image;
        return image;
    }

    private void RefreshDiskImages()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return;

        foreach (PictureBox picture in _pnlDisks.Controls.OfType<Panel>().SelectMany(static t => t.Controls.OfType<PictureBox>()))
            picture.Image = GetDiskImage();
    }

    private void SelectDiskTile(Panel tile, int? preferredPartitionNumber = null)
    {
        if (_selectedDiskTile == tile)
        {
            if (preferredPartitionNumber.HasValue)
                SelectPartitionByNumber(preferredPartitionNumber.Value);
            else if (_selectedPartitionTile != null)
                ClearPartitionSelection();
            return;
        }

        if (_selectedDiskTile != null)
            _selectedDiskTile.BackColor = ShellTheme.ContentBack;

        _selectedDiskTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;

        RebuildPartitionTiles(tile.Tag as ImagingDiskInfo, preferredPartitionNumber);
        UpdateDiskSelectionVisual();
        UpdateSelectedDiskPanel();
    }

    private ImagingDiskInfo? GetSelectedDisk() => _selectedDiskTile?.Tag as ImagingDiskInfo;

    // Partition selector

    private void RebuildPartitionTiles(ImagingDiskInfo? disk, int? preferredPartitionNumber = null)
    {
        ClearPartitionTiles();
        if (disk == null)
            return;

        _pnlPartitions.SuspendLayout();
        try
        {
            foreach (ImagingPartitionInfo partition in disk.Partitions)
            {
                Panel tile = CreatePartitionTile(partition);
                _pnlPartitions.Controls.Add(tile);

                if (preferredPartitionNumber == partition.PartitionNumber)
                    SelectPartitionTile(tile);
            }

        }
        finally
        {
            _pnlPartitions.ResumeLayout(true);
        }

        LayoutPartitionTiles();
    }

    private void ClearPartitionTiles()
    {
        _selectedPartitionTile = null;
        if (_pnlPartitions == null || _pnlPartitions.IsDisposed)
            return;

        while (_pnlPartitions.Controls.Count > 0)
        {
            Control control = _pnlPartitions.Controls[0];
            _pnlPartitions.Controls.RemoveAt(0);
            control.Dispose();
        }
    }

    private Panel CreatePartitionTile(ImagingPartitionInfo partition)
    {
        Panel tile = new()
        {
            Width = _mPx.PartitionTileWidth,
            Height = _mPx.PartitionTileHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = partition
        };

        PictureBox picture = new()
        {
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = GetPartitionImage(partition),
            Cursor = Cursors.Hand
        };

        Label name = new()
        {
            Text = GetPartitionDisplayName(partition),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label sub = new()
        {
            Text = FormatBytes(partition.SizeBytes),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void select(object? _, EventArgs __) => SelectPartitionTile(tile);
        tile.Click += select;
        picture.Click += select;
        name.Click += select;
        sub.Click += select;

        void hover(object? _, EventArgs __)
        {
            if (_selectedPartitionTile != tile)
                tile.BackColor = ShellTheme.ItemHoverBack;
        }

        void leave(object? _, EventArgs __)
        {
            if (tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
                return;
            if (_selectedPartitionTile != tile)
                tile.BackColor = ShellTheme.ContentBack;
        }

        tile.MouseEnter += hover;
        picture.MouseEnter += hover;
        name.MouseEnter += hover;
        sub.MouseEnter += hover;
        tile.MouseLeave += leave;
        picture.MouseLeave += leave;
        name.MouseLeave += leave;
        sub.MouseLeave += leave;

        tile.Controls.Add(picture);
        tile.Controls.Add(name);
        tile.Controls.Add(sub);
        LayoutPartitionTile(tile);
        return tile;
    }

    private void LayoutPartitionTiles()
    {
        if (_pnlPartitions == null || _pnlPartitions.IsDisposed)
            return;

        int tileHeight = Math.Max(_mPx.PartitionTileHeight, _pnlPartitions.ClientSize.Height);
        if (_pnlPartitions.HorizontalScroll.Visible)
            tileHeight = Math.Max(_mPx.PartitionTileHeight, tileHeight - SystemInformation.HorizontalScrollBarHeight);

        foreach (Panel tile in _pnlPartitions.Controls.OfType<Panel>())
        {
            tile.Width = _mPx.PartitionTileWidth;
            tile.Height = tileHeight;
            LayoutPartitionTile(tile);
        }
    }

    private void LayoutPartitionTile(Panel tile)
    {
        PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
        Label[] labels = tile.Controls.OfType<Label>().ToArray();

        if (picture != null)
            SetBoundsIfChanged(
                picture,
                (tile.Width - _mPx.PartitionTileIconSize) / 2,
                _mPx.PartitionTileIconTop,
                _mPx.PartitionTileIconSize,
                _mPx.PartitionTileIconSize);

        if (labels.Length > 0)
            SetBoundsIfChanged(
                labels[0],
                _mPx.PartitionTilePadX,
                _mPx.PartitionTileNameTop,
                Math.Max(0, tile.Width - (_mPx.PartitionTilePadX * 2)),
                _mPx.PartitionTileNameHeight);

        if (labels.Length > 1)
            SetBoundsIfChanged(
                labels[1],
                _mPx.PartitionTilePadX,
                _mPx.PartitionTileSubTop,
                Math.Max(0, tile.Width - (_mPx.PartitionTilePadX * 2)),
                _mPx.PartitionTileSubHeight);
    }

    private Image GetPartitionImage(ImagingPartitionInfo partition)
    {
        int size = _mPx.PartitionTileIconSize;
        DriveVisualKind kind = GetPartitionVisualKind(partition);
        var key = (kind, size);
        if (_partitionImagesByKind.TryGetValue(key, out Image? image))
            return image;

        string imageresPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        int iconIndex = DriveIconMap.GetImageresIconIndex(kind);
        image = IconUtil.FromFileIconIndex(imageresPath, iconIndex, size);
        if (image == null)
        {
            if (kind != DriveVisualKind.Fixed)
            {
                image = GetPartitionImageForKind(DriveVisualKind.Fixed, size);
            }
            else
            {
                using Bitmap fallback = SystemIcons.WinLogo.ToBitmap();
                image = new Bitmap(fallback, new Size(size, size));
            }
        }

        _partitionImagesByKind[key] = image;
        return image;
    }

    private Image GetPartitionImageForKind(DriveVisualKind kind, int size)
    {
        var key = (kind, size);
        if (_partitionImagesByKind.TryGetValue(key, out Image? image))
            return image;

        string imageresPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        int iconIndex = DriveIconMap.GetImageresIconIndex(kind);
        image = IconUtil.FromFileIconIndex(imageresPath, iconIndex, size);
        if (image == null)
        {
            using Bitmap fallback = SystemIcons.WinLogo.ToBitmap();
            image = new Bitmap(fallback, new Size(size, size));
        }

        _partitionImagesByKind[key] = image;
        return image;
    }

    private DriveVisualKind GetPartitionVisualKind(ImagingPartitionInfo partition)
    {
        ImagingBitLockerVolumeInfo? volume = GetBitLockerVolumeForPartition(partition);
        if (volume != null)
        {
            bool isSystemVolume = IsSystemVisualPartition(partition, volume);
            return volume.VisualState switch
            {
                BitLockerVisualState.Locked => DriveVisualKind.BitLockerLocked,
                BitLockerVisualState.Unknown => DriveVisualKind.BitLockerStatusUnknown,
                BitLockerVisualState.ProtectionOff => isSystemVolume
                    ? DriveVisualKind.SystemBitLockerProtectionOff
                    : DriveVisualKind.BitLockerProtectionOff,
                BitLockerVisualState.Unlocked => isSystemVolume
                    ? DriveVisualKind.SystemBitLockerUnlocked
                    : DriveVisualKind.BitLockerUnlocked,
                _ => GetPlainPartitionVisualKind(partition, isSystemVolume)
            };
        }

        return GetPlainPartitionVisualKind(partition, IsSystemVisualPartition(partition, null));
    }

    private static DriveVisualKind GetPlainPartitionVisualKind(ImagingPartitionInfo partition, bool isSystemVolume)
    {
        foreach (string mountPoint in partition.DriveLetters)
        {
            try
            {
                DriveType driveType = new DriveInfo(mountPoint).DriveType;
                if (driveType == DriveType.CDRom)
                    return DriveVisualKind.Optical;
                if (driveType == DriveType.Network)
                    return DriveVisualKind.Network;
                if (driveType == DriveType.Removable)
                    return DriveVisualKind.Removable;
            }
            catch
            {
            }
        }

        return isSystemVolume ? DriveVisualKind.System : DriveVisualKind.Fixed;
    }

    private static bool IsSystemVisualPartition(ImagingPartitionInfo partition, ImagingBitLockerVolumeInfo? volume)
    {
        foreach (string mountPoint in partition.DriveLetters)
        {
            if (DriveSystemDetector.IsSystemVisualDrive(mountPoint))
                return true;
        }

        return volume?.IsSystemVolume == true &&
               !partition.DriveLetters.Any(static mountPoint => DriveSystemDetector.IsRunningSystemDrive(mountPoint));
    }

    private void RefreshPartitionImages()
    {
        if (_pnlPartitions == null || _pnlPartitions.IsDisposed)
            return;

        foreach (Panel tile in _pnlPartitions.Controls.OfType<Panel>())
        {
            if (tile.Tag is not ImagingPartitionInfo partition)
                continue;

            PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
            if (picture != null)
                picture.Image = GetPartitionImage(partition);
        }
    }

    private void SelectPartitionTile(Panel tile)
    {
        if (_selectedPartitionTile == tile)
            return;

        if (_selectedPartitionTile != null)
            _selectedPartitionTile.BackColor = ShellTheme.ContentBack;

        _selectedPartitionTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        UpdateDiskSelectionVisual();
        UpdateSelectedDiskPanel();
    }

    private void ClearPartitionSelection()
    {
        if (_selectedPartitionTile != null)
            _selectedPartitionTile.BackColor = ShellTheme.ContentBack;

        _selectedPartitionTile = null;
        UpdateDiskSelectionVisual();
        UpdateSelectedDiskPanel();
    }

    private void UpdateDiskSelectionVisual()
    {
        if (_selectedDiskTile == null)
            return;

        // Keep the physical disk visibly selected as the parent context, but
        // mute it while a child partition owns the active selection. The
        // shared hover shade provides the same selected-family color at a
        // visibly inactive intensity in both shell themes.
        _selectedDiskTile.BackColor = _selectedPartitionTile == null
            ? ShellTheme.ItemSelectedBack
            : ShellTheme.ItemHoverBack;
    }

    private void SelectPartitionByNumber(int partitionNumber)
    {
        Panel? tile = _pnlPartitions.Controls
            .OfType<Panel>()
            .FirstOrDefault(t => t.Tag is ImagingPartitionInfo p && p.PartitionNumber == partitionNumber);
        if (tile != null)
            SelectPartitionTile(tile);
    }

    private ImagingPartitionInfo? GetSelectedPartition() => _selectedPartitionTile?.Tag as ImagingPartitionInfo;

    private static bool TryGetPartitionCaptureRoot(ImagingPartitionInfo partition, out string root)
    {
        foreach (string drive in partition.DriveLetters)
        {
            string normalized = ImagingPath.NormalizeDriveRoot(drive);
            if (normalized.Length > 0 && Directory.Exists(normalized))
            {
                root = normalized;
                return true;
            }
        }

        root = string.Empty;
        return false;
    }

    private static string GetPartitionDisplayName(ImagingPartitionInfo partition)
    {
        if (partition.DriveLetters.Count > 0)
            return string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')));

        return $"Partition {partition.PartitionNumber}";
    }

    private void UpdateSelectedDiskPanel()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null)
        {
            _txtDiskStatus.Text = "No disk selected";
            _btnCapture.Enabled = false;
            _btnApply.Enabled = false;
            _btnMountWim.Enabled = !_operationActive;
            _btnUnmountWim.Enabled = !_operationActive && _mountedWims.Count > 0;
            _btnCaptureWim.Enabled = false;
            _btnApplyWim.Enabled = false;
            _btnExportWim.Enabled = !_operationActive;
            _btnAddDrivers.Enabled = !_operationActive && _mountedWims.Count > 0;
            _btnUnlock.Enabled = false;
            _btnDeployWim.Enabled = false;
            _btnRefresh.Enabled = !_operationActive;
            UpdateStatusLine();
            return;
        }

        _txtDiskStatus.ForeColor = GetInformationTextColor();
        _lblStatus.ForeColor = GetInformationTextColor();

        ImagingPartitionInfo? partition = GetSelectedPartition();
        bool diskSelectionActive = partition == null;
        _txtDiskStatus.Text = diskSelectionActive
            ? BuildDiskDetails(disk)
            : BuildPartitionDetails(partition!);

        _btnCapture.Enabled = !_operationActive && diskSelectionActive;
        _btnApply.Enabled = !_operationActive && diskSelectionActive;
        _btnMountWim.Enabled = !_operationActive;
        _btnUnmountWim.Enabled = !_operationActive && _mountedWims.Count > 0;
        _btnCaptureWim.Enabled = !_operationActive && !diskSelectionActive;
        _btnApplyWim.Enabled = !_operationActive && !diskSelectionActive;
        _btnExportWim.Enabled = !_operationActive;
        _btnAddDrivers.Enabled = !_operationActive && _mountedWims.Count > 0;
        _btnUnlock.Enabled = !_operationActive &&
                             !diskSelectionActive &&
                             GetBitLockerVolumeForPartition(partition!)?.IsLocked == true;
        _btnDeployWim.Enabled = !_operationActive && diskSelectionActive;
        _btnRefresh.Enabled = !_operationActive;
        UpdateStatusLine();
    }

    private string BuildDiskDetails(ImagingDiskInfo disk)
    {
        StringBuilder text = new();
        text.AppendLine($"Disk {disk.DiskNumber}");
        if (!string.IsNullOrWhiteSpace(disk.Model)) text.AppendLine(disk.Model);
        text.AppendLine($"Size:       {FormatBytes(disk.SizeBytes)}");
        if (!string.IsNullOrWhiteSpace(disk.InterfaceType)) text.AppendLine($"Interface:  {disk.InterfaceType}");
        if (!string.IsNullOrWhiteSpace(disk.SerialNumber)) text.AppendLine($"Serial:     {disk.SerialNumber}");
        text.AppendLine($"Device:     {disk.DevicePath}");
        text.AppendLine();
        text.AppendLine("BitLocker / FFU capture");
        if (!disk.BitLockerStatusAvailable)
        {
            text.AppendLine("  BitLocker status unavailable; encryption state could not be verified.");
            if (!string.IsNullOrWhiteSpace(disk.BitLockerStatusError))
                text.AppendLine($"  Status error: {disk.BitLockerStatusError}");
        }
        else if (disk.BitLockerVolumes.Count == 0)
        {
            text.AppendLine("  No encrypted BitLocker volume detected.");
        }
        else
        {
            foreach (ImagingBitLockerVolumeInfo volume in disk.BitLockerVolumes)
            {
                string percent = volume.EncryptionPercentage.HasValue ? $"{volume.EncryptionPercentage.Value}%" : "unknown %";
                string lockText = volume.IsLocked switch { true => "Locked", false => "Unlocked", _ => "Lock unknown" };
                string conversion = string.IsNullOrWhiteSpace(volume.ConversionStatus) ? "status unknown" : volume.ConversionStatus;
                string encryptionType = string.IsNullOrWhiteSpace(volume.EncryptionType) ? string.Empty : $"  {volume.EncryptionType}";
                text.AppendLine($"  {volume.MountPoint.TrimEnd('\\')}  {conversion}  {percent}{encryptionType}  {lockText}");
            }
        }

        return text.ToString().TrimEnd();
    }

    private string BuildPartitionDetails(ImagingPartitionInfo partition)
    {
        StringBuilder text = new();
        text.AppendLine($"Partition:  {partition.PartitionNumber}");

        string drives = partition.DriveLetters.Count == 0
            ? "None"
            : string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')));
        text.AppendLine($"Drive:      {drives}");
        text.AppendLine($"Size:       {FormatBytes(partition.SizeBytes)}");
        if (!string.IsNullOrWhiteSpace(partition.Type))
            text.AppendLine($"Type:       {partition.Type}");
        text.AppendLine($"Primary:    {(partition.PrimaryPartition ? "Yes" : "No")}");
        text.AppendLine($"Boot:       {(partition.BootPartition ? "Yes" : "No")}");
        if (!string.IsNullOrWhiteSpace(partition.DeviceId))
            text.AppendLine($"Device:     {partition.DeviceId}");

        ImagingBitLockerVolumeInfo? bitLocker = GetBitLockerVolumeForPartition(partition);
        if (bitLocker?.IsBitLockerCapable == true)
        {
            text.AppendLine("BitLocker");
            string state = bitLocker.IsLocked switch
            {
                true => "Locked",
                false => bitLocker.VisualState == BitLockerVisualState.ProtectionOff ? "Unlocked · Protection off" : "Unlocked",
                _ => "Status unknown"
            };
            text.AppendLine($"Status:     {state}");
            if (bitLocker.EncryptionPercentage.HasValue)
                text.AppendLine($"Encrypted:  {bitLocker.EncryptionPercentage.Value}%");
            if (!string.IsNullOrWhiteSpace(bitLocker.ConversionStatus))
                text.AppendLine($"Conversion: {bitLocker.ConversionStatus}");
        }

        return text.ToString().TrimEnd();
    }

    private void UpdateStatusLine()
    {
        bool wasVisible = _lblStatus.Visible;

        if (!string.IsNullOrWhiteSpace(_loadError))
        {
            _lblStatus.Text = _loadError;
            _lblStatus.Visible = true;
        }
        else if (_disks.Count == 0)
        {
            _lblStatus.Text = "No physical disks found.";
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
