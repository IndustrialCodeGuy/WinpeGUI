using Imaging.Core;
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

        _txtDiskStatus = new Label
        {
            AutoSize = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
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
        _btnRefresh.Click += (_, _) => LoadDisks(GetSelectedDisk()?.DiskNumber);

        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _rightPanel.Controls.Add(_txtDiskStatus);
        _rightPanel.Controls.Add(_btnCapture);
        _rightPanel.Controls.Add(_btnApply);
        _rightPanel.Controls.Add(_btnRefresh);
        _rightPanel.Controls.Add(_lblStatus);
        _rightPanel.Resize += (_, _) => LayoutDiskDetails(_rightPanel);

        _splitMain.Panel2.Controls.Add(_rightPanel);
        Controls.Add(_splitMain);

        ApplyChromeFonts();
        LayoutDiskDetails(_rightPanel);
    }

    private void LayoutDiskDetails(Control panel)
    {
        if (panel.ClientSize.Width <= 0 || panel.ClientSize.Height <= 0)
            return;

        int margin = _mPx.DetailMargin;
        int gap = _mPx.DetailGap;
        int buttonWidth = _mPx.DetailButtonWidth;
        int buttonHeight = _mPx.DetailButtonHeight;
        int statusHeight = _mPx.DetailStatusHeight;
        int contentWidth = Math.Max(0, panel.ClientSize.Width - (margin * 2));
        int buttonTop = panel.ClientSize.Height - margin - buttonHeight;
        int statusTop = buttonTop - gap - statusHeight;
        int detailsHeight = Math.Max(0, statusTop - gap - margin);

        SetBoundsIfChanged(_txtDiskStatus, margin, margin, contentWidth, detailsHeight);
        SetBoundsIfChanged(_lblStatus, margin, statusTop, contentWidth, statusHeight);
        SetBoundsIfChanged(_btnCapture, margin, buttonTop, buttonWidth, buttonHeight);
        SetBoundsIfChanged(_btnApply, margin + buttonWidth + gap, buttonTop, buttonWidth, buttonHeight);
        SetBoundsIfChanged(_btnRefresh, margin + ((buttonWidth + gap) * 2), buttonTop, buttonWidth, buttonHeight);
    }

    private void LoadDisks(int? selectDiskNumber = null)
    {
        if (_isLoading || _operationActive)
            return;

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

        RebuildDiskTiles(selectDiskNumber);
        ApplyLayoutMetrics();
        UpdateSelectedDiskPanel();
    }

    private void RebuildDiskTiles(int? selectDiskNumber)
    {
        _pnlDisks.SuspendLayout();
        try
        {
            _pnlDisks.Controls.Clear();
            _selectedDiskTile = null;

            foreach (ImagingDiskInfo disk in _disks)
            {
                Panel tile = CreateDiskTile(disk);
                _pnlDisks.Controls.Add(tile);

                if (selectDiskNumber == disk.DiskNumber)
                    SelectDiskTile(tile);
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
            TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label sub = new()
        {
            Text = FormatBytes(disk.SizeBytes),
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
        int width = _pnlDisks.ClientSize.Width;
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
            string text = $"Disk {disk.DiskNumber}  {FormatBytes(disk.SizeBytes)}";
            int measured = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            desired = Math.Max(desired, measured + (_mPx.DiskTilePadX * 2) + _mPx.DiskPaneBorderAllowance);
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

    private void SelectDiskTile(Panel tile)
    {
        if (_selectedDiskTile == tile)
            return;

        if (_selectedDiskTile != null)
            _selectedDiskTile.BackColor = ShellTheme.ContentBack;

        _selectedDiskTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        UpdateSelectedDiskPanel();
    }

    private ImagingDiskInfo? GetSelectedDisk() => _selectedDiskTile?.Tag as ImagingDiskInfo;

    private void UpdateSelectedDiskPanel()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        if (disk == null)
        {
            _txtDiskStatus.Text = "No disk selected";
            _btnCapture.Enabled = false;
            _btnApply.Enabled = false;
            UpdateStatusLine();
            return;
        }

        _txtDiskStatus.Text = BuildDiskDetails(disk);
        _btnCapture.Enabled = !_operationActive;
        _btnApply.Enabled = !_operationActive;
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
        text.AppendLine("Partitions");

        if (disk.Partitions.Count == 0)
        {
            text.AppendLine("  None detected");
        }
        else
        {
            foreach (ImagingPartitionInfo partition in disk.Partitions)
            {
                string drives = partition.DriveLetters.Count == 0
                    ? string.Empty
                    : "  " + string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')));
                text.AppendLine($"  #{partition.PartitionNumber}  {FormatBytes(partition.SizeBytes),10}{drives}");
            }
        }

        text.AppendLine();
        text.AppendLine("BitLocker / FFU capture");
        FfuCaptureAssessment assessment = FfuCaptureAssessment.Evaluate(disk);
        if (!disk.BitLockerStatusAvailable)
        {
            text.AppendLine("  BitLocker status unavailable; encryption state could not be verified.");
            if (!string.IsNullOrWhiteSpace(disk.BitLockerStatusError))
                text.AppendLine($"  Status error: {disk.BitLockerStatusError}");
            text.AppendLine("  FFU capture readiness: Verify encryption before capture");
        }
        else if (disk.BitLockerVolumes.Count == 0)
        {
            text.AppendLine("  No encrypted BitLocker volume detected.");
            text.AppendLine("  FFU capture readiness: Ready");
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

            text.AppendLine(assessment.RequiresEncryptionWarning
                ? "  FFU capture readiness: Decrypt first (recommended)"
                : "  FFU capture readiness: Ready");
        }

        return text.ToString().TrimEnd();
    }

    private void UpdateStatusLine()
    {
        if (!string.IsNullOrWhiteSpace(_loadError))
        {
            _lblStatus.Text = _loadError;
            _lblStatus.Visible = true;
            return;
        }

        if (_disks.Count == 0)
        {
            _lblStatus.Text = "No physical disks found.";
            _lblStatus.Visible = true;
            return;
        }

        _lblStatus.Text = string.Empty;
        _lblStatus.Visible = false;
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
