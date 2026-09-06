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
            Dock = DockStyle.None,
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
        _pnlDisks.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        InitializeContextActions();


        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            ForeColor = GetInformationTextColor(),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _pnlContextActions.Controls.Add(_lblSelectionContext);
        foreach (ContextAction action in _contextActions)
            _pnlContextActions.Controls.Add(action.Button);

        // The form stays hidden while the initial disk/WIM inventory is loaded.
        // Keep the manually positioned contextual action strip self-sufficient
        // so a late parent/dock resize cannot leave its buttons at default bounds.
        _pnlContextActions.Resize += (_, _) =>
            LayoutContextActionStrip(_mPx.DetailButtonWidth, _mPx.DetailButtonHeight, _mPx.DetailButtonGap);

        _rightPanel.Controls.Add(_pnlDisks);
        _rightPanel.Controls.Add(_lblStatus);
        _rightPanel.Controls.Add(_pnlContextActions);
        _rightPanel.Resize += (_, _) => LayoutDiskDetails(_rightPanel);
        _rightPanel.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        _pnlContextActions.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        _lblSelectionContext.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();
        _lblStatus.MouseDown += (_, _) => DeselectTilesFromNeutralInteraction();

        Controls.Add(_rightPanel);

        // Do not rely on WinForms dock/z-order interaction between the MenuStrip
        // and the imaging content.  Give the content panel explicit bounds below
        // the menu so the normal detail margin can never be painted underneath it.
        LayoutMainContentBelowMenu();

        ApplyChromeFonts();
        LayoutMainContentBelowMenu();
        LayoutDiskDetails(_rightPanel);
    }

    private void LayoutMainContentBelowMenu()
    {
        if (_rightPanel is null || _rightPanel.IsDisposed)
            return;

        int menuHeight = _mainMenu is { IsDisposed: false }
            ? Math.Max(_mainMenu.Height, _mainMenu.PreferredSize.Height)
            : 0;

        SetBoundsIfChanged(
            _rightPanel,
            0,
            menuHeight,
            Math.Max(0, ClientSize.Width),
            Math.Max(0, ClientSize.Height - menuHeight));
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
                return;

            SetWaitCursorState(false);
            Enabled = true;

            if (_operationActive)
                EndOperation();
            else
                UpdateSelectedDiskPanel();

            MessageBox.Show(
                this,
                $"Imaging Manager encountered an unexpected error.\n\n{ex.Message}",
                "Imaging Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
        int rowsTop = margin;
        int contextTop = Math.Max(
            rowsTop + buttonHeight + gap,
            panel.ClientSize.Height - margin - buttonHeight);
        SetBoundsIfChanged(_pnlContextActions, margin, contextTop, fullWidth, buttonHeight);
        LayoutContextActionStrip(buttonWidth, buttonHeight, buttonGap);

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

    private IEnumerable<ContextAction> GetVisibleContextActions() =>
        _contextActions.Where(static action => action.Visible);

    private void LayoutContextActionStrip(int buttonWidth, int buttonHeight, int buttonGap)
    {
        ContextAction[] visibleActions = GetVisibleContextActions().ToArray();
        Button[] visibleButtons = visibleActions.Select(static action => action.Button).ToArray();
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

        ImagingDiskInfo? selectedDisk = selectDiskNumber.HasValue
            ? _disks.FirstOrDefault(disk => disk.DiskNumber == selectDiskNumber.Value)
            : GetSelectedDisk();
        ImagingPartitionInfo? currentPartition = GetSelectedPartition();
        ImagingDiskInfo? currentSelectionDisk = GetSelectedDisk();
        ImagingPartitionInfo? selectedPartition = preferredPartitionNumber.HasValue
            ? selectedDisk?.Partitions.FirstOrDefault(partition => partition.PartitionNumber == preferredPartitionNumber.Value)
            : selectDiskNumber.HasValue
                ? currentSelectionDisk != null &&
                  selectedDisk != null &&
                  string.Equals(currentSelectionDisk.StableIdentity, selectedDisk.StableIdentity, StringComparison.OrdinalIgnoreCase)
                    ? currentPartition
                    : null
                : currentPartition;

        _pendingRefreshDiskIdentity = selectedDisk?.StableIdentity;
        _pendingRefreshDiskNumber = selectDiskNumber ?? selectedDisk?.DiskNumber;
        _pendingRefreshPartitionIdentity = selectedPartition?.StableIdentity;
        _pendingRefreshPartitionNumber = preferredPartitionNumber ?? selectedPartition?.PartitionNumber;
        _pendingRefreshOpticalMountPoint = selectDiskNumber.HasValue || preferredPartitionNumber.HasValue
            ? null
            : GetSelectedOpticalVolume()?.MountPoint;
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

                string? selectDiskIdentity = _pendingRefreshDiskIdentity;
                int? selectDiskNumber = _pendingRefreshDiskNumber;
                string? preferredPartitionIdentity = _pendingRefreshPartitionIdentity;
                int? preferredPartitionNumber = _pendingRefreshPartitionNumber;
                string? preferredOpticalMountPoint = _pendingRefreshOpticalMountPoint;
                string? preferredMountDirectory = _pendingRefreshMountedWimDirectory;

                ImagingInventorySnapshot refreshedInventory;
                string loadError = string.Empty;
                try
                {
                    refreshedInventory = await Task.Run(_inventory.GetInventory);
                }
                catch (Exception ex)
                {
                    refreshedInventory = new ImagingInventorySnapshot();
                    loadError = ex.Message;
                }

                if (IsDisposed || Disposing)
                    return;

                _disks = refreshedInventory.Disks;
                _opticalVolumes = refreshedInventory.OpticalVolumes;
                _loadError = loadError;

                RebuildDiskTiles(
                    selectDiskIdentity,
                    selectDiskNumber,
                    preferredPartitionIdentity,
                    preferredPartitionNumber,
                    preferredOpticalMountPoint,
                    preferredMountDirectory);
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
        string? selectDiskIdentity,
        int? selectDiskNumber,
        string? preferredPartitionIdentity,
        int? preferredPartitionNumber,
        string? preferredOpticalMountPoint = null,
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

            _opticalVolumeRow = null;
            _pnlOpticalVolumes = null;
            _mountedWimRow = null;
            _pnlMountedWims = null;

            Panel? preferredDiskTile = null;
            Panel? preferredPartitionTile = null;

            foreach (ImagingDiskInfo disk in _disks)
            {
                Panel row = CreateDiskRow(disk);
                _pnlDisks.Controls.Add(row);

                bool diskMatches =
                    (!string.IsNullOrWhiteSpace(selectDiskIdentity) &&
                     string.Equals(selectDiskIdentity, disk.StableIdentity, StringComparison.OrdinalIgnoreCase)) ||
                    (string.IsNullOrWhiteSpace(selectDiskIdentity) &&
                     selectDiskNumber.HasValue &&
                     selectDiskNumber.Value == disk.DiskNumber);

                if (row.Tag is not DiskRowContext context || !diskMatches)
                    continue;

                preferredDiskTile = context.DiskTile;
                if (!string.IsNullOrWhiteSpace(preferredPartitionIdentity) || preferredPartitionNumber.HasValue)
                {
                    preferredPartitionTile = context.PartitionStrip.Controls
                        .OfType<Panel>()
                        .FirstOrDefault(tile =>
                        {
                            if (tile.Tag is not PartitionTileContext partitionContext)
                                return false;

                            if (!string.IsNullOrWhiteSpace(preferredPartitionIdentity) &&
                                string.Equals(
                                    preferredPartitionIdentity,
                                    partitionContext.Partition.StableIdentity,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }

                            return string.IsNullOrWhiteSpace(preferredPartitionIdentity) &&
                                   preferredPartitionNumber.HasValue &&
                                   partitionContext.Partition.PartitionNumber == preferredPartitionNumber.Value;
                        });
                }
            }

            if (_opticalVolumes.Count > 0)
            {
                _opticalVolumeRow = CreateOpticalVolumeRow();
                _pnlDisks.Controls.Add(_opticalVolumeRow);
                RebuildOpticalVolumeTiles(preferredOpticalMountPoint, updateUi: false);
            }

            _mountedWimRow = CreateMountedWimRow();
            _pnlDisks.Controls.Add(_mountedWimRow);
            RebuildMountedWimTiles(preferredMountDirectory, updateUi: false);

            if (_selectedMountedWimTile != null || _selectedOpticalVolumeTile != null)
            {
                // The auxiliary-row rebuild restored the previous selection.
            }
            else if (preferredPartitionTile != null)
                SelectPartitionTile(preferredPartitionTile, updateUi: false);
            else if (preferredDiskTile != null)
                SelectDiskTile(preferredDiskTile, updateUi: false);
            else if (!_selectionExplicitlyCleared &&
                     _pnlDisks.Controls.OfType<Panel>()
                         .Select(row => row.Tag as DiskRowContext)
                         .FirstOrDefault(context => context != null)?.DiskTile is Panel firstDiskTile)
                SelectDiskTile(firstDiskTile, updateUi: false);
            else if (!_selectionExplicitlyCleared &&
                     _pnlOpticalVolumes?.Controls.OfType<Panel>().FirstOrDefault() is Panel firstOpticalTile)
                SelectOpticalVolumeTile(firstOpticalTile, updateUi: false);

            LayoutDiskTiles();
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
}
