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
    private Panel CreateDiskRow(ImagingDiskInfo disk)
    {
        Panel row = new()
        {
            Width = GetDiskRowWidth(),
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0, 0, 0, _mPx.DiskRowGap),
            Padding = new Padding(0),
            BackColor = ShellTheme.WindowBack
        };

        row.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        Panel diskTile = CreateDiskTile(disk);
        FlowLayoutPanel partitionStrip = new()
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        partitionStrip.Resize += (_, _) => LayoutPartitionStrip(partitionStrip, disk);
        partitionStrip.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        foreach (ImagingPartitionInfo partition in disk.Partitions)
            partitionStrip.Controls.Add(CreatePartitionTile(disk, partition));

        row.Controls.Add(diskTile);
        row.Controls.Add(partitionStrip);
        row.Tag = new DiskRowContext
        {
            Disk = disk,
            DiskTile = diskTile,
            PartitionStrip = partitionStrip
        };

        LayoutDiskRow(row);
        return row;
    }

    private Panel CreateDiskTile(ImagingDiskInfo disk)
    {
        Panel tile = new()
        {
            Width = _mPx.DiskHeaderWidth,
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
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
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label sub = new()
        {
            Text = FormatBytes(disk.SizeBytes),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label status = new()
        {
            Name = "DiskStatusLabel",
            Text = GetDiskStatusText(disk),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void select(object? _, EventArgs __) => SelectDiskTile(tile);
        WireSelectableTile(tile, select, picture, name, sub, status);

        tile.Controls.Add(picture);
        tile.Controls.Add(name);
        tile.Controls.Add(sub);
        tile.Controls.Add(status);
        LayoutDiskTile(tile);
        return tile;
    }

    private Panel CreatePartitionTile(ImagingDiskInfo disk, ImagingPartitionInfo partition)
    {
        Panel tile = new()
        {
            Width = _mPx.PartitionTileMinimumWidth,
            Height = _mPx.PartitionTileHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = new PartitionTileContext
            {
                Disk = disk,
                Partition = partition
            }
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
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label total = new()
        {
            Text = GetPartitionTotalLine(partition),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        Label? used = null;
        if (partition.DriveLetters.Count > 0)
        {
            used = new Label
            {
                Text = GetPartitionUsedLine(partition),
                ForeColor = ShellTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
        }

        void select(object? _, EventArgs __) => SelectPartitionTile(tile);
        if (used != null)
            WireSelectableTile(tile, select, picture, name, total, used);
        else
            WireSelectableTile(tile, select, picture, name, total);

        tile.Controls.Add(picture);
        tile.Controls.Add(name);
        tile.Controls.Add(total);
        if (used != null)
            tile.Controls.Add(used);
        LayoutPartitionTile(tile);
        return tile;
    }

    private void WireSelectableTile(Panel tile, EventHandler select, params Control[] children)
    {
        tile.Click += select;
        foreach (Control child in children)
            child.Click += select;

        ContextMenuStrip contextMenu = CreateTileContextMenu();
        tile.ContextMenuStrip = contextMenu;
        foreach (Control child in children)
            child.ContextMenuStrip = contextMenu;

        void selectOnRightClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            select(sender, e);
        }

        tile.MouseDown += selectOnRightClick;
        foreach (Control child in children)
            child.MouseDown += selectOnRightClick;

        contextMenu.Opening += (_, e) =>
        {
            select(tile, EventArgs.Empty);
            UpdateSelectedDiskPanel();
            PopulateTileContextMenu(contextMenu);
            e.Cancel = contextMenu.Items.Count == 0;
        };
        tile.Disposed += (_, _) => contextMenu.Dispose();

        void hover(object? _, EventArgs __)
        {
            if (!IsSelectedTile(tile))
                tile.BackColor = ShellTheme.ItemHoverBack;
        }

        void leave(object? _, EventArgs __)
        {
            if (tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position)))
                return;
            if (!IsSelectedTile(tile))
                tile.BackColor = ShellTheme.ContentBack;
        }

        tile.MouseEnter += hover;
        tile.MouseLeave += leave;
        foreach (Control child in children)
        {
            child.MouseEnter += hover;
            child.MouseLeave += leave;
        }
    }

    private ContextMenuStrip CreateTileContextMenu()
    {
        ContextMenuStrip menu = new()
        {
            Renderer = _mainMenu.Renderer,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            ShowImageMargin = false,
            ShowCheckMargin = false
        };

        if (_chromeFont != null)
            menu.Font = _chromeFont;

        return menu;
    }

    private void PopulateTileContextMenu(ContextMenuStrip menu)
    {
        while (menu.Items.Count > 0)
        {
            ToolStripItem item = menu.Items[0];
            menu.Items.RemoveAt(0);
            item.Dispose();
        }

        if (_chromeFont != null)
            menu.Font = _chromeFont;

        ContextAction[] actions = GetVisibleContextActions().ToArray();

        for (int i = 0; i < actions.Length; i++)
        {
            ContextAction action = actions[i];
            if (i == 1 && actions[0] == _actionGetInfo)
                menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem item = new(action.Text)
            {
                Enabled = action.Enabled,
                ForeColor = ShellTheme.TextColor
            };
            if (_chromeFont != null)
                item.Font = _chromeFont;

            item.Click += async (_, _) => await RunContextActionAsync(action);
            menu.Items.Add(item);
        }
    }

    private bool IsSelectedTile(Panel tile) =>
        _selectedDiskTile == tile ||
        _selectedPartitionTile == tile ||
        _selectedOpticalVolumeTile == tile ||
        _selectedMountedWimTile == tile;

    private Panel CreateOpticalVolumeRow()
    {
        Panel row = new()
        {
            Width = GetDiskRowWidth(),
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0, 0, 0, _mPx.DiskRowGap),
            Padding = new Padding(0),
            BackColor = ShellTheme.WindowBack
        };

        row.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        Panel header = new()
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack
        };
        header.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        Label name = new()
        {
            Text = "Optical Media",
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        Label sub = new()
        {
            Text = GetOpticalVolumeCountText(),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        header.Controls.Add(name);
        header.Controls.Add(sub);
        name.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        sub.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        _pnlOpticalVolumes = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pnlOpticalVolumes.Resize += (_, _) => LayoutOpticalVolumeTiles();
        _pnlOpticalVolumes.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        row.Controls.Add(header);
        row.Controls.Add(_pnlOpticalVolumes);
        LayoutOpticalVolumeRow(row);
        return row;
    }

    private string GetOpticalVolumeCountText() => _opticalVolumes.Count switch
    {
        1 => "1 mounted",
        _ => $"{_opticalVolumes.Count} mounted"
    };

    private void RebuildOpticalVolumeTiles(string? preferredMountPoint = null, bool updateUi = true)
    {
        if (_pnlOpticalVolumes == null || _pnlOpticalVolumes.IsDisposed)
            return;

        string? desiredMount = preferredMountPoint ?? GetSelectedOpticalVolume()?.MountPoint;
        _selectedOpticalVolumeTile = null;

        _pnlOpticalVolumes.SuspendLayout();
        try
        {
            while (_pnlOpticalVolumes.Controls.Count > 0)
            {
                Control control = _pnlOpticalVolumes.Controls[0];
                _pnlOpticalVolumes.Controls.RemoveAt(0);
                control.Dispose();
            }

            Panel? preferredTile = null;
            foreach (ImagingVolumeInfo volume in _opticalVolumes)
            {
                Panel tile = CreateOpticalVolumeTile(volume);
                _pnlOpticalVolumes.Controls.Add(tile);
                if (!string.IsNullOrWhiteSpace(desiredMount) &&
                    string.Equals(volume.MountPoint, desiredMount, StringComparison.OrdinalIgnoreCase))
                {
                    preferredTile = tile;
                }
            }

            LayoutOpticalVolumeTiles();
            if (preferredTile != null)
                SelectOpticalVolumeTile(preferredTile, updateUi);
        }
        finally
        {
            _pnlOpticalVolumes.ResumeLayout(true);
        }
    }

    private Panel CreateOpticalVolumeTile(ImagingVolumeInfo volume)
    {
        Panel tile = new()
        {
            Width = 1,
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = volume
        };

        PictureBox picture = new()
        {
            SizeMode = PictureBoxSizeMode.CenterImage,
            Image = GetPartitionImageForKind(DriveVisualKind.Optical, _mPx.PartitionTileIconSize),
            Cursor = Cursors.Hand
        };

        string root = volume.MountPoint.TrimEnd('\\');
        Label name = new()
        {
            Text = string.IsNullOrWhiteSpace(volume.VolumeLabel)
                ? $"{root} — CD Drive"
                : $"{root} — {volume.VolumeLabel}",
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        string details = volume.TotalSizeBytes > 0
            ? $"Total: {FormatBytes(volume.TotalSizeBytes)}"
            : "Total: —";
        Label sub = new()
        {
            Text = details,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void select(object? _, EventArgs __) => SelectOpticalVolumeTile(tile);
        WireSelectableTile(tile, select, picture, name, sub);

        tile.Controls.Add(picture);
        tile.Controls.Add(name);
        tile.Controls.Add(sub);
        LayoutOpticalVolumeTile(tile);
        return tile;
    }

    private Panel CreateMountedWimRow()
    {
        Panel row = new()
        {
            Width = GetDiskRowWidth(),
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = ShellTheme.WindowBack
        };
        row.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        Panel header = new()
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack
        };
        header.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        Label name = new()
        {
            Text = "Mounted WIMs",
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        Label sub = new()
        {
            Text = GetMountedWimCountText(),
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        header.Controls.Add(name);
        header.Controls.Add(sub);
        name.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        sub.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        _pnlMountedWims = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = ShellTheme.ContentBack,
            BorderStyle = BorderStyle.FixedSingle
        };
        _pnlMountedWims.Resize += (_, _) => LayoutMountedWimTiles();
        _pnlMountedWims.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        row.Controls.Add(header);
        row.Controls.Add(_pnlMountedWims);
        LayoutMountedWimRow(row);
        return row;
    }

    private string GetMountedWimCountText() => _mountedWims.Count switch
    {
        0 => "None",
        1 => "1 image",
        _ => $"{_mountedWims.Count} images"
    };

    private void RebuildMountedWimTiles(string? preferredMountDirectory = null, bool updateUi = true)
    {
        if (_pnlMountedWims == null || _pnlMountedWims.IsDisposed)
            return;

        string? desiredMount = preferredMountDirectory ?? GetSelectedMountedWim()?.MountDirectory;
        _selectedMountedWimTile = null;

        _pnlMountedWims.SuspendLayout();
        try
        {
            while (_pnlMountedWims.Controls.Count > 0)
            {
                Control control = _pnlMountedWims.Controls[0];
                _pnlMountedWims.Controls.RemoveAt(0);
                control.Dispose();
            }

            Panel? preferredTile = null;
            foreach (WimMountedImageInfo image in _mountedWims)
            {
                Panel tile = CreateMountedWimTile(image);
                _pnlMountedWims.Controls.Add(tile);
                if (!string.IsNullOrWhiteSpace(desiredMount) &&
                    string.Equals(image.MountDirectory, desiredMount, StringComparison.OrdinalIgnoreCase))
                    preferredTile = tile;
            }

            if (_mountedWimRow?.Controls.OfType<Panel>().FirstOrDefault() is Panel header)
            {
                Label[] labels = header.Controls.OfType<Label>().ToArray();
                if (labels.Length > 1)
                    labels[1].Text = GetMountedWimCountText();
            }

            LayoutMountedWimTiles();
            if (preferredTile != null)
                SelectMountedWimTile(preferredTile, updateUi);
        }
        finally
        {
            _pnlMountedWims.ResumeLayout(true);
        }
    }

    private Panel CreateMountedWimTile(WimMountedImageInfo image)
    {
        Panel tile = new()
        {
            Width = 1,
            Height = _mPx.DiskRowHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = image
        };

        string file = string.IsNullOrWhiteSpace(image.ImageFile) ? "Mounted WIM" : Path.GetFileName(image.ImageFile);
        string index = image.ImageIndex > 0 ? $" — Index {image.ImageIndex}" : string.Empty;
        Label name = new()
        {
            Text = file + index,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };
        string mountStatus = GetMountedWimAbnormalStatus(image);
        string mountLine = string.IsNullOrWhiteSpace(mountStatus)
            ? image.MountDirectory
            : $"{image.MountDirectory} — {mountStatus}";
        Label sub = new()
        {
            Text = mountLine,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };

        void select(object? _, EventArgs __) => SelectMountedWimTile(tile);
        WireSelectableTile(tile, select, name, sub);

        tile.Controls.Add(name);
        tile.Controls.Add(sub);
        LayoutMountedWimTile(tile);
        return tile;
    }

    private void LayoutDiskTiles()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return;

        int width = GetDiskRowWidth();
        foreach (Panel row in _pnlDisks.Controls.OfType<Panel>())
        {
            row.Width = width;
            row.Height = _mPx.DiskRowHeight;
            if (row.Tag is DiskRowContext)
                LayoutDiskRow(row);
            else if (row == _opticalVolumeRow)
                LayoutOpticalVolumeRow(row);
            else if (row == _mountedWimRow)
                LayoutMountedWimRow(row);
        }

    }

    private int GetDiskRowWidth()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return Math.Max(1, _mPx.ContentMinimumWidth);

        int width = _pnlDisks.ClientSize.Width - _pnlDisks.Padding.Left - _pnlDisks.Padding.Right;
        int contentHeight = _pnlDisks.Controls.OfType<Panel>()
            .Sum(static row => row.Height + row.Margin.Vertical);
        bool needsVerticalScroll = contentHeight > _pnlDisks.ClientSize.Height;
        if (needsVerticalScroll)
            width -= SystemInformation.VerticalScrollBarWidth;

        return Math.Max(1, width);
    }

    private void LayoutDiskRow(Panel row)
    {
        if (row.Tag is not DiskRowContext context)
            return;

        int gap = _mPx.DiskRowInnerGap;
        SetBoundsIfChanged(context.DiskTile, 0, 0, _mPx.DiskHeaderWidth, _mPx.DiskRowHeight);
        SetBoundsIfChanged(
            context.PartitionStrip,
            _mPx.DiskHeaderWidth + gap,
            0,
            Math.Max(1, row.ClientSize.Width - _mPx.DiskHeaderWidth - gap),
            _mPx.DiskRowHeight);

        LayoutDiskTile(context.DiskTile);
        LayoutPartitionStrip(context.PartitionStrip, context.Disk);
    }

    private void LayoutOpticalVolumeRow(Panel row) =>
        LayoutAuxiliaryRow(row, _pnlOpticalVolumes, LayoutOpticalVolumeTiles);

    private void LayoutMountedWimRow(Panel row) =>
        LayoutAuxiliaryRow(row, _pnlMountedWims, LayoutMountedWimTiles);

    private void LayoutAuxiliaryRow(
        Panel row,
        FlowLayoutPanel? contentPanel,
        Action layoutTiles)
    {
        if (contentPanel == null)
            return;

        Panel? header = row.Controls.OfType<Panel>().FirstOrDefault(panel => panel != contentPanel);
        if (header == null)
            return;

        int gap = _mPx.DiskRowInnerGap;
        SetBoundsIfChanged(header, 0, 0, _mPx.DiskHeaderWidth, _mPx.DiskRowHeight);
        SetBoundsIfChanged(
            contentPanel,
            _mPx.DiskHeaderWidth + gap,
            0,
            Math.Max(1, row.ClientSize.Width - _mPx.DiskHeaderWidth - gap),
            _mPx.DiskRowHeight);

        Label[] labels = header.Controls.OfType<Label>().ToArray();
        int textLeft = _mPx.DiskTilePadX;
        int textWidth = Math.Max(0, header.ClientSize.Width - (_mPx.DiskTilePadX * 2));
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], textLeft, _mPx.DiskTileNameTop, textWidth, _mPx.DiskTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], textLeft, _mPx.DiskTileSubTop, textWidth, _mPx.DiskTileSubHeight);

        layoutTiles();
    }

    private void LayoutDiskTile(Panel tile)
    {
        PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
        Label[] labels = tile.Controls.OfType<Label>().ToArray();
        int iconLeft = _mPx.DiskTilePadX;
        int nameLeft = iconLeft + _mPx.DiskTileIconSize + _mPx.DiskTileTextGap;
        int nameWidth = Math.Max(0, tile.ClientSize.Width - nameLeft - _mPx.DiskTilePadX);
        int lineLeft = _mPx.DiskTilePadX;
        int lineWidth = Math.Max(0, tile.ClientSize.Width - (_mPx.DiskTilePadX * 2));

        if (picture != null)
            SetBoundsIfChanged(picture, iconLeft, _mPx.DiskTileIconTop, _mPx.DiskTileIconSize, _mPx.DiskTileIconSize);
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], nameLeft, _mPx.DiskTileNameTop, nameWidth, _mPx.DiskTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], lineLeft, _mPx.DiskTileSubTop, lineWidth, _mPx.DiskTileSubHeight);
        if (labels.Length > 2)
            SetBoundsIfChanged(labels[2], lineLeft, _mPx.DiskTileStatusTop, lineWidth, _mPx.DiskTileStatusHeight);
    }

    private void LayoutPartitionStrip(FlowLayoutPanel strip, ImagingDiskInfo disk)
    {
        Panel[] tiles = strip.Controls.OfType<Panel>().ToArray();
        if (tiles.Length == 0)
            return;

        int minWidth = _mPx.PartitionTileMinimumWidth;
        int available = Math.Max(1, strip.ClientSize.Width);
        int totalMinimum = minWidth * tiles.Length;
        double totalSize = disk.Partitions.Sum(static p => (double)p.SizeBytes);
        int extra = Math.Max(0, available - totalMinimum);
        int allocated = 0;

        for (int i = 0; i < tiles.Length; i++)
        {
            Panel tile = tiles[i];
            ulong partitionSize = tile.Tag is PartitionTileContext context ? context.Partition.SizeBytes : 0;
            int width = minWidth;
            if (extra > 0 && totalSize > 0)
            {
                if (i == tiles.Length - 1)
                    width += Math.Max(0, extra - allocated);
                else
                {
                    int share = (int)Math.Round(extra * (partitionSize / (double)totalSize));
                    share = Math.Clamp(share, 0, Math.Max(0, extra - allocated));
                    width += share;
                    allocated += share;
                }
            }

            tile.Width = width;
            tile.Height = Math.Max(1, strip.ClientSize.Height);
            LayoutPartitionTile(tile);
        }
    }

    private void LayoutPartitionTile(Panel tile)
    {
        PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
        Label[] labels = tile.Controls.OfType<Label>().ToArray();
        int iconLeft = _mPx.PartitionTilePadX;
        int nameLeft = iconLeft + _mPx.PartitionTileIconSize + _mPx.PartitionTileTextGap;
        int nameWidth = Math.Max(0, tile.ClientSize.Width - nameLeft - _mPx.PartitionTilePadX);
        int lineLeft = _mPx.PartitionTilePadX;
        int lineWidth = Math.Max(0, tile.ClientSize.Width - (_mPx.PartitionTilePadX * 2));

        if (picture != null)
            SetBoundsIfChanged(picture, iconLeft, _mPx.PartitionTileIconTop, _mPx.PartitionTileIconSize, _mPx.PartitionTileIconSize);
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], nameLeft, _mPx.PartitionTileNameTop, nameWidth, _mPx.PartitionTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], lineLeft, _mPx.PartitionTileSubTop, lineWidth, _mPx.PartitionTileSubHeight);
        if (labels.Length > 2)
            SetBoundsIfChanged(labels[2], lineLeft, _mPx.PartitionTileUsedTop, lineWidth, _mPx.PartitionTileUsedHeight);
    }

    private void LayoutOpticalVolumeTiles() =>
        LayoutEvenTiles(_pnlOpticalVolumes, LayoutOpticalVolumeTile);

    private void LayoutMountedWimTiles() =>
        LayoutEvenTiles(_pnlMountedWims, LayoutMountedWimTile);

    private static void LayoutEvenTiles(
        FlowLayoutPanel? panel,
        Action<Panel> layoutTile)
    {
        if (panel == null || panel.IsDisposed)
            return;

        Panel[] tiles = panel.Controls.OfType<Panel>().ToArray();
        if (tiles.Length == 0)
            return;

        int tileHeight = Math.Max(1, panel.ClientSize.Height);
        int available = Math.Max(1, panel.ClientSize.Width);
        int baseWidth = Math.Max(1, available / tiles.Length);
        int remainder = Math.Max(0, available - (baseWidth * tiles.Length));

        for (int i = 0; i < tiles.Length; i++)
        {
            Panel tile = tiles[i];
            tile.Width = baseWidth + (i < remainder ? 1 : 0);
            tile.Height = tileHeight;
            layoutTile(tile);
        }
    }

    private void LayoutOpticalVolumeTile(Panel tile)
    {
        PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
        Label[] labels = tile.Controls.OfType<Label>().ToArray();
        int iconLeft = _mPx.PartitionTilePadX;
        int nameLeft = iconLeft + _mPx.PartitionTileIconSize + _mPx.PartitionTileTextGap;
        int nameWidth = Math.Max(0, tile.ClientSize.Width - nameLeft - _mPx.PartitionTilePadX);
        int lineLeft = _mPx.PartitionTilePadX;
        int lineWidth = Math.Max(0, tile.ClientSize.Width - (_mPx.PartitionTilePadX * 2));

        if (picture != null)
            SetBoundsIfChanged(picture, iconLeft, _mPx.PartitionTileIconTop, _mPx.PartitionTileIconSize, _mPx.PartitionTileIconSize);
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], nameLeft, _mPx.PartitionTileNameTop, nameWidth, _mPx.PartitionTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], lineLeft, _mPx.PartitionTileSubTop, lineWidth, _mPx.PartitionTileSubHeight);
    }

    private void LayoutMountedWimTile(Panel tile)
    {
        Label[] labels = tile.Controls.OfType<Label>().ToArray();
        int left = _mPx.PartitionTilePadX;
        int width = Math.Max(0, tile.ClientSize.Width - (_mPx.PartitionTilePadX * 2));
        if (labels.Length > 0)
            SetBoundsIfChanged(labels[0], left, _mPx.PartitionTileNameTop, width, _mPx.PartitionTileNameHeight);
        if (labels.Length > 1)
            SetBoundsIfChanged(labels[1], left, _mPx.PartitionTileSubTop, width, _mPx.PartitionTileSubHeight);
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

        foreach (Panel row in _pnlDisks.Controls.OfType<Panel>())
        {
            if (row.Tag is not DiskRowContext context)
                continue;

            PictureBox? picture = context.DiskTile.Controls.OfType<PictureBox>().FirstOrDefault();
            if (picture != null)
                picture.Image = GetDiskImage();
        }
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
        foreach (ImagingVolumeInfo partitionVolume in partition.Volumes)
        {
            if (partitionVolume.DriveType == DriveType.CDRom)
                return DriveVisualKind.Optical;
            if (partitionVolume.DriveType == DriveType.Network)
                return DriveVisualKind.Network;
            if (partitionVolume.DriveType == DriveType.Removable)
                return DriveVisualKind.Removable;
        }

        return isSystemVolume ? DriveVisualKind.System : DriveVisualKind.Fixed;
    }

    private static bool IsSystemVisualPartition(ImagingPartitionInfo partition, ImagingBitLockerVolumeInfo? volume)
    {
        bool hasSystemVisualVolume = PlatformDetect.IsWinPE
            ? partition.Volumes.Any(static item => item.ContainsOfflineWindowsInstall)
            : partition.Volumes.Any(static item => item.IsRunningSystemDrive);
        if (hasSystemVisualVolume)
            return true;

        return volume?.IsSystemVolume == true &&
               !partition.Volumes.Any(static item => item.IsRunningSystemDrive);
    }

    private void RefreshPartitionImages()
    {
        if (_pnlDisks == null || _pnlDisks.IsDisposed)
            return;

        foreach (Panel row in _pnlDisks.Controls.OfType<Panel>())
        {
            if (row.Tag is not DiskRowContext context)
                continue;

            foreach (Panel tile in context.PartitionStrip.Controls.OfType<Panel>())
            {
                if (tile.Tag is not PartitionTileContext partitionContext)
                    continue;

                PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
                if (picture != null)
                    picture.Image = GetPartitionImage(partitionContext.Partition);
            }
        }

        if (_pnlOpticalVolumes != null && !_pnlOpticalVolumes.IsDisposed)
        {
            Image opticalImage = GetPartitionImageForKind(DriveVisualKind.Optical, _mPx.PartitionTileIconSize);
            foreach (Panel tile in _pnlOpticalVolumes.Controls.OfType<Panel>())
            {
                PictureBox? picture = tile.Controls.OfType<PictureBox>().FirstOrDefault();
                if (picture != null)
                    picture.Image = opticalImage;
            }
        }
    }

    private void TrimImageCachesForCurrentDpi()
    {
        Image[] obsoleteDiskImages = _diskImagesBySize
            .Where(pair => pair.Key != _mPx.DiskTileIconSize)
            .Select(static pair => pair.Value)
            .Distinct()
            .ToArray();
        foreach (int key in _diskImagesBySize.Keys.Where(key => key != _mPx.DiskTileIconSize).ToArray())
            _diskImagesBySize.Remove(key);

        Image[] obsoletePartitionImages = _partitionImagesByKind
            .Where(pair => pair.Key.Size != _mPx.PartitionTileIconSize)
            .Select(static pair => pair.Value)
            .Distinct()
            .ToArray();
        foreach (var key in _partitionImagesByKind.Keys
                     .Where(key => key.Size != _mPx.PartitionTileIconSize)
                     .ToArray())
        {
            _partitionImagesByKind.Remove(key);
        }

        HashSet<Image> retained = _diskImagesBySize.Values
            .Concat(_partitionImagesByKind.Values)
            .ToHashSet();

        foreach (Image image in obsoleteDiskImages.Concat(obsoletePartitionImages).Distinct())
        {
            if (!retained.Contains(image))
                image.Dispose();
        }
    }
}
