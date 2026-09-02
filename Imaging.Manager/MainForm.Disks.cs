using BitLocker.Core;
using Imaging.Core;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using System.Text;

namespace Imaging.Manager;

public partial class MainForm
{
    private sealed class DiskRowContext
    {
        public required ImagingDiskInfo Disk { get; init; }
        public required Panel DiskTile { get; init; }
        public required FlowLayoutPanel PartitionStrip { get; init; }
    }

    private sealed class PartitionTileContext
    {
        public required ImagingDiskInfo Disk { get; init; }
        public required ImagingPartitionInfo Partition { get; init; }
    }

    private sealed class VerticalOnlyFlowLayoutPanel : FlowLayoutPanel
    {
        private const int SbHorz = 0;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SuppressHorizontalScrollBar();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            SuppressHorizontalScrollBar();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            SuppressHorizontalScrollBar();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            SuppressHorizontalScrollBar();
        }

        private void SuppressHorizontalScrollBar()
        {
            if (IsHandleCreated)
                ShowScrollBar(Handle, SbHorz, false);
        }
    }

    private void InitializeDiskUi()
    {
        _rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ShellTheme.WindowBack
        };

        _pnlGlobalActions = new Panel
        {
            BackColor = ShellTheme.WindowBack
        };

        _pnlContextActions = new Panel
        {
            BackColor = ShellTheme.WindowBack
        };

        _lblSelectionContext = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };

        _pnlDisks = new VerticalOnlyFlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0),
            BackColor = ShellTheme.WindowBack,
            BorderStyle = BorderStyle.None
        };
        _pnlDisks.HorizontalScroll.Enabled = false;
        _pnlDisks.Resize += (_, _) => LayoutDiskTiles();

        _btnCapture = CreateActionButton("Capture FFU", async () => await CaptureSelectedDiskAsync());
        _btnApply = CreateActionButton("Apply FFU", async () => await ApplyToSelectedDiskAsync());
        _btnMountWim = CreateActionButton("Mount WIM", async () => await MountWimAsync());
        _btnUnmountWim = CreateActionButton("Unmount WIM", async () => await UnmountWimAsync());
        _btnCaptureWim = CreateActionButton("Capture WIM", async () => await CaptureSelectedPartitionWimAsync());
        _btnApplyWim = CreateActionButton("Apply WIM", async () => await ApplyWimToSelectedPartitionAsync());
        _btnDeployWim = CreateActionButton("Deploy WIM", async () => await DeployWimToSelectedDiskAsync());
        _btnExportWim = CreateActionButton("Export WIM", async () => await ExportWimAsync());
        _btnAddDrivers = CreateActionButton("Add Drivers", async () => await AddDriversAsync());
        _btnGetInfo = CreateActionButton("Get Info", () =>
        {
            ShowSelectedInfo();
            return Task.CompletedTask;
        });
        _btnUnlock = CreateActionButton("Unlock", async () => await UnlockSelectedPartitionAsync());
        _btnRefresh = CreateActionButton("Refresh", async () => await RefreshViewAsync());

        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = GetInformationTextColor(),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _pnlGlobalActions.Controls.Add(_btnMountWim);
        _pnlGlobalActions.Controls.Add(_btnExportWim);
        _pnlGlobalActions.Controls.Add(_btnRefresh);

        _pnlContextActions.Controls.Add(_lblSelectionContext);
        _pnlContextActions.Controls.Add(_btnGetInfo);
        _pnlContextActions.Controls.Add(_btnCapture);
        _pnlContextActions.Controls.Add(_btnApply);
        _pnlContextActions.Controls.Add(_btnDeployWim);
        _pnlContextActions.Controls.Add(_btnCaptureWim);
        _pnlContextActions.Controls.Add(_btnApplyWim);
        _pnlContextActions.Controls.Add(_btnUnmountWim);
        _pnlContextActions.Controls.Add(_btnAddDrivers);
        _pnlContextActions.Controls.Add(_btnUnlock);

        _rightPanel.Controls.Add(_pnlGlobalActions);
        _rightPanel.Controls.Add(_pnlDisks);
        _rightPanel.Controls.Add(_lblStatus);
        _rightPanel.Controls.Add(_pnlContextActions);
        _rightPanel.Resize += (_, _) => LayoutDiskDetails(_rightPanel);

        Controls.Add(_rightPanel);

        ApplyChromeFonts();
        LayoutDiskDetails(_rightPanel);
    }

    private Button CreateActionButton(string text, Func<Task> action)
    {
        Button button = new()
        {
            Text = text,
            UseVisualStyleBackColor = true
        };
        button.Click += async (_, _) => await action();
        return button;
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

        int fullWidth = Math.Max(0, panel.ClientSize.Width - (margin * 2));
        int globalTop = margin;
        SetBoundsIfChanged(_pnlGlobalActions, margin, globalTop, fullWidth, buttonHeight);
        LayoutGlobalActionStrip(buttonWidth, buttonHeight, buttonGap);

        int contextTop = Math.Max(
            globalTop + buttonHeight + gap,
            panel.ClientSize.Height - margin - buttonHeight);
        SetBoundsIfChanged(_pnlContextActions, margin, contextTop, fullWidth, buttonHeight);
        LayoutContextActionStrip(buttonWidth, buttonHeight, buttonGap);

        int rowsTop = globalTop + buttonHeight + gap;
        int rowsBottom = contextTop - gap;
        int statusTop = rowsBottom;
        if (_lblStatus.Visible)
        {
            statusTop = Math.Max(rowsTop, rowsBottom - statusHeight);
            rowsBottom = Math.Max(rowsTop, statusTop - gap);
        }

        SetBoundsIfChanged(_pnlDisks, margin, rowsTop, fullWidth, Math.Max(0, rowsBottom - rowsTop));
        SetBoundsIfChanged(_lblStatus, margin, statusTop, fullWidth, statusHeight);

        LayoutDiskTiles();
    }

    private void LayoutGlobalActionStrip(int buttonWidth, int buttonHeight, int buttonGap)
    {
        int left = 0;
        SetBoundsIfChanged(_btnMountWim, left, 0, buttonWidth, buttonHeight);
        left += buttonWidth + buttonGap;
        SetBoundsIfChanged(_btnExportWim, left, 0, buttonWidth, buttonHeight);

        int refreshLeft = Math.Max(0, _pnlGlobalActions.ClientSize.Width - buttonWidth);
        SetBoundsIfChanged(_btnRefresh, refreshLeft, 0, buttonWidth, buttonHeight);
    }

    private void LayoutContextActionStrip(int buttonWidth, int buttonHeight, int buttonGap)
    {
        Button[] orderedButtons =
        {
            _btnGetInfo,
            _btnCapture,
            _btnApply,
            _btnDeployWim,
            _btnCaptureWim,
            _btnApplyWim,
            _btnUnmountWim,
            _btnAddDrivers,
            _btnUnlock
        };

        Button[] visibleButtons = orderedButtons.Where(static button => button.Visible).ToArray();
        int buttonsWidth = visibleButtons.Length == 0
            ? 0
            : (visibleButtons.Length * buttonWidth) + ((visibleButtons.Length - 1) * buttonGap);
        int buttonsLeft = Math.Max(0, _pnlContextActions.ClientSize.Width - buttonsWidth);

        int labelWidth = Math.Max(0, buttonsLeft - (visibleButtons.Length > 0 ? _mPx.DetailGap : 0));
        SetBoundsIfChanged(_lblSelectionContext, 0, 0, labelWidth, buttonHeight);

        int left = buttonsLeft;
        foreach (Button button in visibleButtons)
        {
            SetBoundsIfChanged(button, left, 0, buttonWidth, buttonHeight);
            left += buttonWidth + buttonGap;
        }
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
            ClearSelectionVisuals();
            while (_pnlDisks.Controls.Count > 0)
            {
                Control control = _pnlDisks.Controls[0];
                _pnlDisks.Controls.RemoveAt(0);
                control.Dispose();
            }

            _mountedWimRow = null;
            _pnlMountedWims = null;

            Panel? preferredDiskTile = null;
            Panel? preferredPartitionTile = null;

            foreach (ImagingDiskInfo disk in _disks)
            {
                Panel row = CreateDiskRow(disk);
                _pnlDisks.Controls.Add(row);

                if (row.Tag is not DiskRowContext context || selectDiskNumber != disk.DiskNumber)
                    continue;

                preferredDiskTile = context.DiskTile;
                if (preferredPartitionNumber.HasValue)
                {
                    preferredPartitionTile = context.PartitionStrip.Controls
                        .OfType<Panel>()
                        .FirstOrDefault(tile => tile.Tag is PartitionTileContext partitionContext &&
                                                partitionContext.Partition.PartitionNumber == preferredPartitionNumber.Value);
                }
            }

            _mountedWimRow = CreateMountedWimRow();
            _pnlDisks.Controls.Add(_mountedWimRow);
            RebuildMountedWimTiles();

            if (preferredPartitionTile != null)
                SelectPartitionTile(preferredPartitionTile);
            else if (preferredDiskTile != null)
                SelectDiskTile(preferredDiskTile);
            else if (_pnlDisks.Controls.OfType<Panel>()
                         .Select(row => row.Tag as DiskRowContext)
                         .FirstOrDefault(context => context != null)?.DiskTile is Panel firstDiskTile)
                SelectDiskTile(firstDiskTile);
        }
        finally
        {
            _pnlDisks.ResumeLayout(true);
        }
    }

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
            Text = disk.IsOffline switch
            {
                true => "Offline",
                false => "Online",
                _ => "Status unknown"
            },
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

    private bool IsSelectedTile(Panel tile) =>
        _selectedDiskTile == tile || _selectedPartitionTile == tile || _selectedMountedWimTile == tile;

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

        Panel header = new()
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack
        };
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

    private void RebuildMountedWimTiles(string? preferredMountDirectory = null)
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
                SelectMountedWimTile(preferredTile);
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
            Width = _mPx.MountedWimTileWidth,
            Height = _mPx.PartitionTileHeight,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ShellTheme.ContentBack,
            ForeColor = ShellTheme.TextColor,
            Cursor = Cursors.Hand,
            Tag = image
        };

        string file = string.IsNullOrWhiteSpace(image.ImageFile) ? "Mounted WIM" : Path.GetFileName(image.ImageFile);
        string index = image.ImageIndex > 0 ? $" [{image.ImageIndex}]" : string.Empty;
        Label name = new()
        {
            Text = file + index,
            ForeColor = ShellTheme.TextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
            Cursor = Cursors.Hand
        };
        Label sub = new()
        {
            Text = image.MountDirectory,
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

    private void LayoutMountedWimRow(Panel row)
    {
        if (_pnlMountedWims == null)
            return;

        Panel? header = row.Controls.OfType<Panel>().FirstOrDefault(panel => panel != _pnlMountedWims);
        if (header == null)
            return;

        int gap = _mPx.DiskRowInnerGap;
        SetBoundsIfChanged(header, 0, 0, _mPx.DiskHeaderWidth, _mPx.DiskRowHeight);
        SetBoundsIfChanged(
            _pnlMountedWims,
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

        LayoutMountedWimTiles();
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

    private void LayoutMountedWimTiles()
    {
        if (_pnlMountedWims == null || _pnlMountedWims.IsDisposed)
            return;

        foreach (Panel tile in _pnlMountedWims.Controls.OfType<Panel>())
        {
            tile.Width = _mPx.MountedWimTileWidth;
            tile.Height = _mPx.PartitionTileHeight;
            LayoutMountedWimTile(tile);
        }
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
    }

    private void SelectDiskTile(Panel tile)
    {
        if (_selectedDiskTile == tile && _selectedPartitionTile == null && _selectedMountedWimTile == null)
            return;

        ClearMountedWimSelection();
        ClearDiskAndPartitionSelection();
        _selectedDiskTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        UpdateSelectedDiskPanel();
    }

    private void SelectPartitionTile(Panel tile)
    {
        if (tile.Tag is not PartitionTileContext)
            return;

        if (_selectedPartitionTile == tile && _selectedMountedWimTile == null)
            return;

        ClearMountedWimSelection();
        ClearDiskAndPartitionSelection();
        _selectedPartitionTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        UpdateSelectedDiskPanel();
    }

    private void SelectMountedWimTile(Panel tile)
    {
        if (_selectedMountedWimTile == tile)
            return;

        ClearDiskAndPartitionSelection();
        ClearMountedWimSelection();
        _selectedMountedWimTile = tile;
        tile.BackColor = ShellTheme.ItemSelectedBack;
        UpdateSelectedDiskPanel();
    }

    private void ClearSelectionVisuals()
    {
        ClearDiskAndPartitionSelection();
        ClearMountedWimSelection();
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
        string driveSuffix = partition.DriveLetters.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", partition.DriveLetters.Select(static d => d.TrimEnd('\\')))})";
        return $"Partition {partition.PartitionNumber}{driveSuffix}";
    }

    private static string GetPartitionTotalLine(ImagingPartitionInfo partition) =>
        $"Total: {FormatBytes(partition.SizeBytes)}";

    private static string GetPartitionUsedLine(ImagingPartitionInfo partition)
    {
        foreach (string drive in partition.DriveLetters)
        {
            try
            {
                string root = ImagingPath.NormalizeDriveRoot(drive);
                if (root.Length == 0)
                    continue;

                DriveInfo info = new(root);
                if (!info.IsReady || info.TotalSize <= 0)
                    continue;

                long used = Math.Max(0, info.TotalSize - info.TotalFreeSpace);
                return $"Used: {FormatBytes((ulong)used)}";
            }
            catch
            {
            }
        }

        return "Used: —";
    }

    private void UpdateSelectedDiskPanel()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        WimMountedImageInfo? mountedWim = GetSelectedMountedWim();

        bool diskSelectionActive = disk != null && partition == null && mountedWim == null;
        bool partitionSelectionActive = disk != null && partition != null && mountedWim == null;
        bool mountedWimSelectionActive = mountedWim != null;
        bool partitionIsOfflineWindows = partitionSelectionActive &&
                                         TryGetOfflineWindowsRoot(partition!, out _);
        bool partitionIsLocked = partitionSelectionActive &&
                                 GetBitLockerVolumeForPartition(partition!)?.IsLocked == true;

        _lblStatus.ForeColor = GetInformationTextColor();

        _btnMountWim.Visible = true;
        _btnExportWim.Visible = true;
        _btnRefresh.Visible = true;
        _btnMountWim.Enabled = !_operationActive;
        _btnExportWim.Enabled = !_operationActive;
        _btnRefresh.Enabled = !_operationActive;

        _btnGetInfo.Visible = diskSelectionActive || partitionSelectionActive || mountedWimSelectionActive;
        _btnCapture.Visible = diskSelectionActive;
        _btnApply.Visible = diskSelectionActive;
        _btnDeployWim.Visible = diskSelectionActive;
        _btnCaptureWim.Visible = partitionSelectionActive;
        _btnApplyWim.Visible = partitionSelectionActive;
        _btnUnmountWim.Visible = mountedWimSelectionActive;
        _btnAddDrivers.Visible = mountedWimSelectionActive || partitionIsOfflineWindows;
        _btnUnlock.Visible = partitionIsLocked;

        _btnGetInfo.Enabled = !_operationActive && _btnGetInfo.Visible;
        _btnCapture.Enabled = !_operationActive && diskSelectionActive;
        _btnApply.Enabled = !_operationActive && diskSelectionActive;
        _btnDeployWim.Enabled = !_operationActive && diskSelectionActive;
        _btnCaptureWim.Enabled = !_operationActive && partitionSelectionActive;
        _btnApplyWim.Enabled = !_operationActive && partitionSelectionActive;
        _btnUnmountWim.Enabled = !_operationActive && mountedWimSelectionActive;
        _btnAddDrivers.Enabled = !_operationActive &&
                                 ((mountedWimSelectionActive && mountedWim!.ReadWrite) ||
                                  partitionIsOfflineWindows);
        _btnUnlock.Enabled = !_operationActive && partitionIsLocked;

        _lblSelectionContext.Text = mountedWimSelectionActive
            ? mountedWim!.DisplayName
            : partitionSelectionActive
                ? GetPartitionDisplayName(partition!)
                : diskSelectionActive
                    ? $"Disk {disk!.DiskNumber}"
                    : "Select a disk, partition, or mounted WIM";

        LayoutContextActionStrip(_mPx.DetailButtonWidth, _mPx.DetailButtonHeight, _mPx.DetailButtonGap);
        UpdateStatusLine();
    }

    private static bool TryGetOfflineWindowsRoot(ImagingPartitionInfo partition, out string root)
    {
        if (!TryGetPartitionCaptureRoot(partition, out root))
            return false;

        if (DriveSystemDetector.IsRunningSystemDrive(root) ||
            !DriveSystemDetector.ContainsOfflineWindowsInstall(root))
        {
            root = string.Empty;
            return false;
        }

        return true;
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
        else if (GetSelectedPartition() is ImagingPartitionInfo partition)
        {
            title = $"{GetPartitionDisplayName(partition)} Information";
            details = BuildPartitionDetails(partition);
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
        text.AppendLine($"Disk {disk.DiskNumber}");
        if (!string.IsNullOrWhiteSpace(disk.Model)) text.AppendLine(disk.Model);
        text.AppendLine($"Size:       {FormatBytes(disk.SizeBytes)}");
        text.AppendLine($"Status:     {disk.IsOffline switch { true => "Offline", false => "Online", _ => "Unknown" }}");
        if (!string.IsNullOrWhiteSpace(disk.InterfaceType)) text.AppendLine($"Interface:  {disk.InterfaceType}");
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

    private static string BuildMountedWimDetails(WimMountedImageInfo image)
    {
        StringBuilder text = new();
        text.AppendLine($"Image:       {image.ImageFile}");
        if (image.ImageIndex > 0)
            text.AppendLine($"Index:       {image.ImageIndex}");
        text.AppendLine($"Mount:       {image.MountDirectory}");
        text.AppendLine($"Mode:        {(image.ReadWrite ? "Read/write" : "Read-only")}");
        text.AppendLine($"Status:      {(string.IsNullOrWhiteSpace(image.Status) ? "Unknown" : image.Status)}");
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
