using BitLocker.Core;
using Shared.Shell.Models;
using Shared.Shell.Utilities;
using Shared.Shell.Theming;

namespace BitLocker.Manager;

public partial class MainForm
{
    // UI construction and layout

    private void InitializeVolumeUi()
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

        _pnlVolumes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pnlVolumes.Resize += (_, _) => LayoutVolumeTiles();

        _splitMain.Panel1.Controls.Add(_pnlVolumes);

        _rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.WindowBack
        };

        _txtVolumeStatus = new Label
        {
            Left = _mPx.DetailMargin,
            Top = _mPx.DetailMargin,
            Width = 0,
            Height = 0,
            AutoSize = false,
            Font = _statusFont ?? Font,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
            Text = "No drive selected"
        };

        _btnUnlock = new Button
        {
            Left = _mPx.DetailMargin,
            Top = 0,
            Width = _mPx.DetailButtonWidth,
            Height = _mPx.DetailButtonHeight,
            Text = "Unlock Drive",
            UseVisualStyleBackColor = true
        };

        _btnUnlock.Click += async (_, _) =>
        {
            BitLockerVolumeInfo? volume = GetSelectedVolume();
            if (volume == null)
                return;

            await LaunchUnlockWindowAsync(volume);
        };

        _btnLock = new Button
        {
            Left = _mPx.DetailMargin + _mPx.DetailButtonWidth + _mPx.DetailGap,
            Top = 0,
            Width = _mPx.DetailButtonWidth,
            Height = _mPx.DetailButtonHeight,
            Text = "Lock Drive",
            UseVisualStyleBackColor = true
        };

        _btnLock.Click += (_, _) =>
        {
            BitLockerVolumeInfo? volume = GetSelectedVolume();
            if (volume == null)
                return;

            ExecuteLock(volume, this);
        };

        _btnRefresh = new Button
        {
            Left = _mPx.DetailMargin + ((_mPx.DetailButtonWidth + _mPx.DetailGap) * 2),
            Top = 0,
            Width = _mPx.DetailButtonWidth,
            Height = _mPx.DetailButtonHeight,
            Text = "Refresh",
            UseVisualStyleBackColor = true
        };
        _btnRefresh.Click += (_, _) => RefreshSelectedVolumePanel();

        _lblStatus = new Label
        {
            Left = _mPx.DetailMargin,
            Top = 0,
            Width = 0,
            Height = _mPx.DetailStatusHeight,
            AutoSize = false,
            AutoEllipsis = true,
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _rightPanel.Controls.Add(_txtVolumeStatus);
        _rightPanel.Controls.Add(_btnUnlock);
        _rightPanel.Controls.Add(_btnLock);
        _rightPanel.Controls.Add(_btnRefresh);
        _rightPanel.Controls.Add(_lblStatus);

        _rightPanel.Resize += (_, _) => LayoutVolumeDetails(_rightPanel);

        _splitMain.Panel2.Controls.Add(_rightPanel);
        Controls.Add(_splitMain);

        ApplyChromeFonts();
        LayoutVolumeDetails(_rightPanel);
    }

    private void LayoutVolumeDetails(Control rightPanel)
    {
        if (rightPanel.ClientSize.Width <= 0 || rightPanel.ClientSize.Height <= 0)
            return;

        int margin = _mPx.DetailMargin;
        int gap = _mPx.DetailGap;
        int buttonWidth = _mPx.DetailButtonWidth;
        int buttonHeight = _mPx.DetailButtonHeight;
        int statusHeight = _mPx.DetailStatusHeight;

        int contentWidth = Math.Max(0, rightPanel.ClientSize.Width - (margin * 2));

        int buttonTop = rightPanel.ClientSize.Height - margin - buttonHeight;
        int statusTop = buttonTop - gap - statusHeight;
        int detailsHeight = Math.Max(0, statusTop - gap - margin);

        SetBoundsIfChanged(
            _txtVolumeStatus,
            margin,
            margin,
            contentWidth,
            detailsHeight);

        SetBoundsIfChanged(
            _lblStatus,
            margin,
            statusTop,
            contentWidth,
            statusHeight);

        SetBoundsIfChanged(
            _btnUnlock,
            margin,
            buttonTop,
            buttonWidth,
            buttonHeight);

        SetBoundsIfChanged(
            _btnLock,
            margin + buttonWidth + gap,
            buttonTop,
            buttonWidth,
            buttonHeight);

        SetBoundsIfChanged(
            _btnRefresh,
            margin + ((buttonWidth + gap) * 2),
            buttonTop,
            buttonWidth,
            buttonHeight);
    }

    // Data refresh and rebinding

    private void LoadVolumes(bool selectLaunchDrive)
    {
        if (_isLoadingVolumes)
            return;

        _isLoadingVolumes = true;

        try
        {
            _volumeLoadError = string.Empty;

            try
            {
                _volumes = _backend.GetVolumes();
                RebindVolumeList();

                bool launchDriveFound = true;
                if (selectLaunchDrive)
                    launchDriveFound = SelectLaunchDriveIfPresent();

                UpdateSelectedVolumePanel();

                if (selectLaunchDrive &&
                    !string.IsNullOrWhiteSpace(_launchArgs.DrivePath) &&
                    !launchDriveFound)
                {
                    SetTextIfChanged(_lblStatus, $"The requested drive '{_launchArgs.DrivePath}' was not found.");
                    SetVisibleIfChanged(_lblStatus, true);
                }
            }
            catch (Exception ex)
            {
                _volumes = Array.Empty<BitLockerVolumeInfo>();
                _volumeLoadError = ex.Message;
                bool selectedVolumePanelUpdated = RebindVolumeList();

                if (!selectedVolumePanelUpdated)
                    UpdateSelectedVolumePanel();
            }
        }
        finally
        {
            _isLoadingVolumes = false;
        }
    }

    private bool RebindVolumeList()
    {
        string? selectedMountPoint = GetSelectedVolume()?.MountPoint;
        bool selectedVolumePanelUpdated = false;

        _pnlVolumes.SuspendLayout();
        try
        {
            while (_pnlVolumes.Controls.Count > 0)
            {
                Control control = _pnlVolumes.Controls[0];
                _pnlVolumes.Controls.RemoveAt(0);
                control.Dispose();
            }

            _selectedVolumeTile = null;

            foreach (BitLockerVolumeInfo volume in _volumes)
            {
                Panel tile = CreateVolumeTile(volume);
                _pnlVolumes.Controls.Add(tile);

                if (!selectedVolumePanelUpdated &&
                    !string.IsNullOrWhiteSpace(selectedMountPoint) &&
                    string.Equals(volume.MountPoint, selectedMountPoint, StringComparison.OrdinalIgnoreCase))
                {
                    SelectVolumeTile(tile);
                    selectedVolumePanelUpdated = true;
                }
            }
        }
        finally
        {
            _pnlVolumes.ResumeLayout();
        }

        ApplyLayoutMetrics();

        if (_selectedVolumeTile == null && _pnlVolumes.Controls.Count > 0)
        {
            SelectVolumeTile((Panel)_pnlVolumes.Controls[0]);
            selectedVolumePanelUpdated = true;
        }

        return selectedVolumePanelUpdated;
    }

    private void LayoutVolumeTiles()
    {
        if (_pnlVolumes == null || _pnlVolumes.IsDisposed)
            return;

        int tileWidth = GetVolumeTileWidth();

        foreach (Control control in _pnlVolumes.Controls)
        {
            if (control is not Panel tile)
                continue;

            if (tile.Width != tileWidth)
                tile.Width = tileWidth;

            if (tile.Height != _mPx.VolumeTileHeight)
                tile.Height = _mPx.VolumeTileHeight;

            foreach (Control child in tile.Controls)
            {
                if (child is PictureBox picture)
                    SetBoundsIfChanged(
                        picture,
                        (tile.Width - _mPx.VolumeTileIconSize) / 2,
                        _mPx.VolumeTileIconTop,
                        _mPx.VolumeTileIconSize,
                        _mPx.VolumeTileIconSize);
                else if (child is Label name)
                    SetBoundsIfChanged(
                        name,
                        _mPx.VolumeTileNamePadX,
                        _mPx.VolumeTileNameTop,
                        Math.Max(0, tile.Width - (_mPx.VolumeTileNamePadX * 2)),
                        _mPx.VolumeTileNameHeight);
            }
        }
    }

    private int GetVolumeTileWidth()
    {
        int width = _pnlVolumes.ClientSize.Width - _pnlVolumes.Padding.Left - _pnlVolumes.Padding.Right;

        if (_pnlVolumes.VerticalScroll.Visible)
            width -= SystemInformation.VerticalScrollBarWidth;

        return Math.Max(_mPx.MinimumVolumeTileWidth, width);
    }

    private int GetDesiredVolumePaneWidth()
    {
        int desiredWidth = _mPx.LeftPanelWidth;
        int iconWidth = _mPx.VolumeTileIconSize + (_mPx.VolumeTileNamePadX * 2);
        desiredWidth = Math.Max(desiredWidth, iconWidth + _mPx.VolumePaneBorderAllowance);

        Font measureFont = _chromeFont ?? Font;
        foreach (BitLockerVolumeInfo volume in _volumes)
        {
            if (string.IsNullOrWhiteSpace(volume.DisplayName))
                continue;

            int textWidth = TextRenderer.MeasureText(
                volume.DisplayName,
                measureFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

            desiredWidth = Math.Max(
                desiredWidth,
                textWidth + (_mPx.VolumeTileNamePadX * 2) + _mPx.VolumePaneBorderAllowance);
        }

        if (VolumePaneNeedsVerticalScrollBar())
            desiredWidth += _mPx.VolumePaneScrollBarAllowance;

        return Math.Clamp(
            desiredWidth,
            _mPx.VolumePaneMinimumWidth,
            _mPx.VolumePaneMaximumWidth);
    }

    private bool VolumePaneNeedsVerticalScrollBar()
    {
        if (_pnlVolumes == null || _pnlVolumes.IsDisposed)
            return false;

        int availableHeight = _pnlVolumes.ClientSize.Height;
        if (availableHeight <= 0)
            return false;

        return _volumes.Count * _mPx.VolumeTileHeight > availableHeight;
    }

    private Panel CreateVolumeTile(BitLockerVolumeInfo volume)
    {
        int tileWidth = GetVolumeTileWidth();

        Panel tile = new()
        {
            Width = tileWidth,
            Height = _mPx.VolumeTileHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.None,
            BackColor = ShellTheme.ContentBack,
            Cursor = Cursors.Hand,
            Tag = volume
        };

        PictureBox picture = new()
        {
            Left = (tile.Width - _mPx.VolumeTileIconSize) / 2,
            Top = _mPx.VolumeTileIconTop,
            Width = _mPx.VolumeTileIconSize,
            Height = _mPx.VolumeTileIconSize,
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = GetDriveImage(GetDriveVisualKind(volume)),
            Cursor = Cursors.Hand
        };

        Label name = new()
        {
            Left = _mPx.VolumeTileNamePadX,
            Top = _mPx.VolumeTileNameTop,
            Width = Math.Max(0, tile.Width - (_mPx.VolumeTileNamePadX * 2)),
            Height = _mPx.VolumeTileNameHeight,
            Font = _chromeFont ?? Font,
            Text = volume.DisplayName,
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void selectHandler(object? _, EventArgs __)
        {
            SelectVolumeTile(tile);
        }

        tile.Click += selectHandler;
        picture.Click += selectHandler;
        name.Click += selectHandler;

        void hoverHandler(object? _, EventArgs __)
        {
            if (_selectedVolumeTile != tile)
                tile.BackColor = ShellTheme.ItemHoverBack;
        }

        void leaveHandler(object? _, EventArgs __)
        {
            if (tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
                return;

            if (_selectedVolumeTile != tile)
                tile.BackColor = ShellTheme.ContentBack;
        }

        tile.MouseEnter += hoverHandler;
        picture.MouseEnter += hoverHandler;
        name.MouseEnter += hoverHandler;

        tile.MouseLeave += leaveHandler;
        picture.MouseLeave += leaveHandler;
        name.MouseLeave += leaveHandler;

        tile.Controls.Add(picture);
        tile.Controls.Add(name);

        return tile;
    }

    // Drive imagery

    private Image GetDriveImage(DriveVisualKind kind)
    {
        int iconSize = _mPx.VolumeTileIconSize;
        var key = (kind, iconSize);

        if (_driveImagesByKind.TryGetValue(key, out Image? image))
            return image;

        string imageresPath = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        int iconIndex = DriveIconMap.GetImageresIconIndex(kind);

        image = IconUtil.FromFileIconIndex(imageresPath, iconIndex, iconSize);

        if (image == null)
        {
            if (kind != DriveVisualKind.Fixed)
            {
                image = GetDriveImage(DriveVisualKind.Fixed);
            }
            else
            {
                using Bitmap fallback = SystemIcons.WinLogo.ToBitmap();
                image = new Bitmap(fallback, new Size(iconSize, iconSize));
            }
        }

        _driveImagesByKind[key] = image;
        return image;
    }

    private static DriveVisualKind GetDriveVisualKind(BitLockerVolumeInfo volume)
    {
        bool isSystemVolume = IsSystemVolume(volume);

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
            _ => GetPlainDriveVisualKind(volume, isSystemVolume)
        };
    }

    private static DriveVisualKind GetPlainDriveVisualKind(BitLockerVolumeInfo volume, bool isSystemVolume)
    {
        DriveType driveType = GetDriveType(volume.MountPoint);

        if (driveType == DriveType.CDRom)
            return DriveVisualKind.Optical;

        if (driveType == DriveType.Network)
            return DriveVisualKind.Network;

        if (driveType == DriveType.Removable ||
            string.Equals(volume.VolumeTypeText, "Removable", StringComparison.OrdinalIgnoreCase))
        {
            return DriveVisualKind.Removable;
        }

        if (isSystemVolume)
            return DriveVisualKind.System;

        return DriveVisualKind.Fixed;
    }

    private static bool IsSystemVolume(BitLockerVolumeInfo volume)
    {
        if (DriveSystemDetector.IsSystemVisualDrive(volume.MountPoint))
            return true;

        return volume.IsSystemVolume &&
               !DriveSystemDetector.IsRunningSystemDrive(volume.MountPoint);
    }

    private static DriveType GetDriveType(string mountPoint)
    {
        try
        {
            return new DriveInfo(mountPoint).DriveType;
        }
        catch
        {
            return DriveType.Unknown;
        }
    }

    // Selection and detail state

    private void SelectVolumeTile(Panel tile)
    {
        if (_selectedVolumeTile == tile)
            return;

        if (_selectedVolumeTile != null)
        {
            _selectedVolumeTile.BackColor = ShellTheme.ContentBack;
        }

        _selectedVolumeTile = tile;
        _selectedVolumeTile.BackColor = ShellTheme.ItemSelectedBack;

        UpdateSelectedVolumePanel();
    }

    private bool SelectLaunchDriveIfPresent()
    {
        Panel? tile = FindVolumeTile(_launchArgs.DrivePath);
        if (tile == null)
            return false;

        SelectVolumeTile(tile);
        _pnlVolumes.ScrollControlIntoView(tile);
        return true;
    }

    private Panel? FindVolumeTile(string? mountPoint)
    {
        if (string.IsNullOrWhiteSpace(mountPoint))
            return null;

        foreach (Control control in _pnlVolumes.Controls)
        {
            if (control is not Panel tile || tile.Tag is not BitLockerVolumeInfo volume)
                continue;

            if (string.Equals(volume.MountPoint, mountPoint, StringComparison.OrdinalIgnoreCase))
                return tile;
        }

        return null;
    }

    private BitLockerVolumeInfo? GetSelectedVolume()
    {
        return _selectedVolumeTile?.Tag as BitLockerVolumeInfo;
    }

    private void UpdateSelectedVolumePanel()
    {
        BitLockerVolumeInfo? volume = GetSelectedVolume();

        if (volume == null)
        {
            SetTextIfChanged(_txtVolumeStatus, "No drive selected");
            SetEnabledIfChanged(_btnUnlock, false);
            SetEnabledIfChanged(_btnLock, false);
            UpdateVolumeStatusText();
            return;
        }

        SetTextIfChanged(
            _txtVolumeStatus,
            string.IsNullOrWhiteSpace(volume.StatusText)
                ? volume.DisplayName
                : volume.StatusText);

        SetEnabledIfChanged(_btnUnlock, volume.IsLocked == true);
        SetEnabledIfChanged(_btnLock, volume.IsLocked == false && volume.IsBitLockerCapable && volume.ProtectionOn && !volume.IsSystemVolume);

        UpdateVolumeStatusText();
    }

    // Refresh after unlock/lock while preserving the selected tile when possible.
    private void RefreshSelectedVolumePanel()
    {
        string? selectedMountPoint = GetSelectedVolume()?.MountPoint;

        LoadVolumes(selectLaunchDrive: false);

        Panel? selectedTile = FindVolumeTile(selectedMountPoint);
        if (selectedTile != null)
            _pnlVolumes.ScrollControlIntoView(selectedTile);
    }

    private void UpdateVolumeStatusText()
    {
        if (!string.IsNullOrWhiteSpace(_volumeLoadError))
        {
            SetTextIfChanged(_lblStatus, _volumeLoadError);
            SetVisibleIfChanged(_lblStatus, true);
            return;
        }

        if (_volumes.Count == 0)
        {
            SetTextIfChanged(_lblStatus, "No BitLocker volumes found.");
            SetVisibleIfChanged(_lblStatus, true);
            return;
        }

        SetTextIfChanged(_lblStatus, string.Empty);
        SetVisibleIfChanged(_lblStatus, false);
    }
}
