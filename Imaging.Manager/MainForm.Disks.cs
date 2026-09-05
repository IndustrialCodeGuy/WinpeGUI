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
        private const int WmSetRedraw = 0x000B;

        public VerticalOnlyFlowLayoutPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
        }

        public void SetRedrawEnabled(bool enabled)
        {
            if (!IsHandleCreated)
                return;

            User32.SendMessage(
                Handle,
                WmSetRedraw,
                enabled ? new IntPtr(1) : IntPtr.Zero,
                IntPtr.Zero);

            if (enabled)
            {
                Invalidate(invalidateChildren: true);
                Update();
            }
        }

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
        _btnRemountWim = CreateActionButton("Remount WIM", async () => await RemountWimAsync());
        _btnCleanupMounts = CreateActionButton("Cleanup Mounts", async () => await CleanupMountsAsync());
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
        _pnlGlobalActions.Controls.Add(_btnCleanupMounts);
        _pnlGlobalActions.Controls.Add(_btnRefresh);

        _pnlContextActions.Controls.Add(_lblSelectionContext);
        _pnlContextActions.Controls.Add(_btnGetInfo);
        _pnlContextActions.Controls.Add(_btnCapture);
        _pnlContextActions.Controls.Add(_btnApply);
        _pnlContextActions.Controls.Add(_btnDeployWim);
        _pnlContextActions.Controls.Add(_btnCaptureWim);
        _pnlContextActions.Controls.Add(_btnApplyWim);
        _pnlContextActions.Controls.Add(_btnUnmountWim);
        _pnlContextActions.Controls.Add(_btnRemountWim);
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
        Button[] leftButtons =
        {
            _btnMountWim,
            _btnExportWim,
            _btnCleanupMounts
        };

        int left = 0;
        foreach (Button button in leftButtons.Where(static button => button.Visible))
        {
            SetBoundsIfChanged(button, left, 0, buttonWidth, buttonHeight);
            left += buttonWidth + buttonGap;
        }

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
            _btnRemountWim,
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

    private Task RequestDiskRefreshAsync(
        int? selectDiskNumber = null,
        int? preferredPartitionNumber = null,
        string? preferredMountDirectory = null)
    {
        if (IsDisposed || Disposing)
            return Task.CompletedTask;

        _pendingRefreshDiskNumber = selectDiskNumber ?? GetSelectedDisk()?.DiskNumber;
        _pendingRefreshPartitionNumber = preferredPartitionNumber ?? GetSelectedPartition()?.PartitionNumber;
        _pendingRefreshMountedWimDirectory = preferredMountDirectory ?? GetSelectedMountedWim()?.MountDirectory;
        _diskRefreshPending = true;

        if (_diskRefreshTask is null || _diskRefreshTask.IsCompleted)
            _diskRefreshTask = RunDiskRefreshLoopAsync();

        return _diskRefreshTask;
    }

    private async Task RunDiskRefreshLoopAsync()
    {
        _diskRefreshInProgress = true;
        UpdateSelectedDiskPanel();

        try
        {
            while (_diskRefreshPending && !IsDisposed && !Disposing)
            {
                _diskRefreshPending = false;

                int? selectDiskNumber = _pendingRefreshDiskNumber;
                int? preferredPartitionNumber = _pendingRefreshPartitionNumber;
                string? preferredMountDirectory = _pendingRefreshMountedWimDirectory;

                IReadOnlyList<ImagingDiskInfo> refreshedDisks;
                string loadError = string.Empty;
                try
                {
                    refreshedDisks = await Task.Run(_inventory.GetDisks);
                }
                catch (Exception ex)
                {
                    refreshedDisks = Array.Empty<ImagingDiskInfo>();
                    loadError = ex.Message;
                }

                if (IsDisposed || Disposing)
                    return;

                _disks = refreshedDisks;
                _loadError = loadError;

                RebuildDiskTiles(
                    selectDiskNumber,
                    preferredPartitionNumber,
                    preferredMountDirectory);
                ApplyLayoutMetrics();
                UpdateSelectedDiskPanel();
            }
        }
        finally
        {
            _diskRefreshInProgress = false;
            if (!IsDisposed && !Disposing)
                UpdateSelectedDiskPanel();
        }
    }

    private void RebuildDiskTiles(
        int? selectDiskNumber,
        int? preferredPartitionNumber,
        string? preferredMountDirectory = null)
    {
        _pnlDisks.SetRedrawEnabled(false);
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
            RebuildMountedWimTiles(preferredMountDirectory);

            if (_selectedMountedWimTile != null)
            {
                // RebuildMountedWimTiles restored the mounted-image selection.
            }
            else if (preferredPartitionTile != null)
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
            try
            {
                _pnlDisks.ResumeLayout(true);
            }
            finally
            {
                _pnlDisks.SetRedrawEnabled(true);
            }
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

        Panel[] tiles = _pnlMountedWims.Controls.OfType<Panel>().ToArray();
        if (tiles.Length == 0)
            return;

        int tileHeight = Math.Max(1, _pnlMountedWims.ClientSize.Height);
        int available = Math.Max(1, _pnlMountedWims.ClientSize.Width);
        int baseWidth = Math.Max(1, available / tiles.Length);
        int remainder = Math.Max(0, available - (baseWidth * tiles.Length));

        for (int i = 0; i < tiles.Length; i++)
        {
            Panel tile = tiles[i];
            tile.Width = baseWidth + (i < remainder ? 1 : 0);
            tile.Height = tileHeight;
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
        WimMountedImageInfo? mountedWim = GetSelectedMountedWim();

        bool diskSelectionActive = disk != null && partition == null && mountedWim == null;
        bool partitionSelectionActive = disk != null && partition != null && mountedWim == null;
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

        _btnMountWim.Visible = true;
        _btnExportWim.Visible = true;
        _btnCleanupMounts.Visible = true;
        _btnRefresh.Visible = true;
        _btnMountWim.Enabled = actionsAvailable;
        _btnExportWim.Enabled = actionsAvailable;
        _btnCleanupMounts.Enabled = actionsAvailable && anyInvalidMountedWim;
        _btnRefresh.Enabled = actionsAvailable;

        _btnGetInfo.Visible = diskSelectionActive || partitionSelectionActive || mountedWimSelectionActive;
        _btnCapture.Visible = diskSelectionActive;
        _btnApply.Visible = diskSelectionActive;
        _btnDeployWim.Visible = diskSelectionActive;
        _btnCaptureWim.Visible = partitionSelectionActive;
        _btnApplyWim.Visible = partitionSelectionActive;
        _btnUnmountWim.Text = mountedWimPendingUnmount ? "Finish Unmount" : "Unmount WIM";
        _btnUnmountWim.Visible = mountedWimHealthyOrUnknown || mountedWimPendingUnmount;
        _btnRemountWim.Visible = mountedWimNeedsRemount;
        _btnAddDrivers.Visible = mountedWimHealthyOrUnknown || partitionIsOfflineWindows;
        _btnUnlock.Visible = partitionIsLocked;

        _btnGetInfo.Enabled = actionsAvailable && _btnGetInfo.Visible;
        _btnCapture.Enabled = actionsAvailable && diskSelectionActive;
        _btnApply.Enabled = actionsAvailable && diskSelectionActive;
        _btnDeployWim.Enabled = actionsAvailable && diskSelectionActive;
        _btnCaptureWim.Enabled = actionsAvailable && partitionSelectionActive;
        _btnApplyWim.Enabled = actionsAvailable && partitionSelectionActive;
        _btnUnmountWim.Enabled = actionsAvailable && (mountedWimHealthyOrUnknown || mountedWimPendingUnmount);
        _btnRemountWim.Enabled = actionsAvailable && mountedWimNeedsRemount;
        _btnAddDrivers.Enabled = actionsAvailable &&
                                 ((mountedWimHealthyOrUnknown && mountedWim!.ReadWrite) ||
                                  partitionIsOfflineWindows);
        _btnUnlock.Enabled = actionsAvailable && partitionIsLocked;

        string selectionBusySuffix = selectedDiskBusy
            ? $" — {selectedDiskOperationName} in progress"
            : string.Empty;

        _lblSelectionContext.Text = mountedWimSelectionActive
            ? mountedWim!.DisplayName
            : partitionSelectionActive
                ? GetPartitionDisplayName(partition!) + selectionBusySuffix
                : diskSelectionActive
                    ? $"Disk {disk!.DiskNumber}" + selectionBusySuffix
                    : "Select a disk, partition, or mounted WIM";

        LayoutGlobalActionStrip(_mPx.DetailButtonWidth, _mPx.DetailButtonHeight, _mPx.DetailButtonGap);
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
        if (partition.DriveLetters.Count == 0)
            return;

        bool wroteSection = false;
        foreach (string drive in partition.DriveLetters)
        {
            try
            {
                string root = ImagingPath.NormalizeDriveRoot(drive);
                if (root.Length == 0)
                    continue;

                DriveInfo info = new(root);
                if (!wroteSection)
                {
                    AppendInfoSection(text, "Volume");
                    wroteSection = true;
                }

                text.AppendLine(root.TrimEnd('\\'));
                AppendInfoLine(text, "  Ready", info.IsReady ? "Yes" : "No");
                AppendInfoLine(text, "  Drive type", info.DriveType.ToString());
                if (!info.IsReady)
                    continue;

                AppendInfoLine(text, "  Label", info.VolumeLabel);
                AppendInfoLine(text, "  File system", info.DriveFormat);
                AppendInfoLine(text, "  Total", FormatBytes((ulong)Math.Max(0, info.TotalSize)));
                AppendInfoLine(text, "  Used", FormatBytes((ulong)Math.Max(0, info.TotalSize - info.TotalFreeSpace)));
                AppendInfoLine(text, "  Free", FormatBytes((ulong)Math.Max(0, info.TotalFreeSpace)));
                AppendInfoLine(text, "  Available free", FormatBytes((ulong)Math.Max(0, info.AvailableFreeSpace)));
            }
            catch
            {
                // The partition may be locked or otherwise inaccessible in WinPE.
            }
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
            _lblStatus.Text = "Loading disk and mounted WIM inventory...";
            _lblStatus.Visible = true;
        }
        else if (!string.IsNullOrWhiteSpace(_loadError))
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
