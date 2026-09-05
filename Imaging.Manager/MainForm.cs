using Imaging.Core;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using Shell.Infrastructure.Coordination;

namespace Imaging.Manager;

public partial class MainForm : Form
{
    private readonly DiskInventory _inventory = new();
    private readonly DismFfuBackend _ffuBackend = new();
    private readonly DismWimBackend _wimBackend = new();
    private readonly TemporaryDriveLetterService _temporaryDriveLetters = new();
    private readonly DriveLetterReassignmentService _driveLetterReassignment;
    private readonly WimDeploymentService _wimDeployment;
    private readonly WinReStagingService _winReStaging;
    private readonly PartitionFormatService _partitionFormatter = new();
    private readonly ImagingOperationCoordinator _operationCoordinator = new();
    private StorageChangeCoordinator? _storageChangeCoordinator;
    private CancellationTokenSource? _activationCts;
    private Task? _activationTask;

    private VerticalOnlyFlowLayoutPanel _pnlDisks = null!;
    private Panel _rightPanel = null!;
    private Panel _pnlGlobalActions = null!;
    private Panel _pnlContextActions = null!;
    private Label _lblSelectionContext = null!;
    private Panel? _mountedWimRow;
    private FlowLayoutPanel? _pnlMountedWims;
    private Label _lblStatus = null!;
    private Button _btnCapture = null!;
    private Button _btnApply = null!;
    private Button _btnRefresh = null!;
    private Button _btnMountWim = null!;
    private Button _btnUnmountWim = null!;
    private Button _btnRemountWim = null!;
    private Button _btnCleanupMounts = null!;
    private Button _btnCaptureWim = null!;
    private Button _btnApplyWim = null!;
    private Button _btnExportWim = null!;
    private Button _btnAddDrivers = null!;
    private Button _btnUnlock = null!;
    private Button _btnDeployWim = null!;
    private Button _btnGetInfo = null!;

    private Panel? _selectedDiskTile;
    private Panel? _selectedPartitionTile;
    private Panel? _selectedMountedWimTile;
    private IReadOnlyList<ImagingDiskInfo> _disks = Array.Empty<ImagingDiskInfo>();
    private readonly Dictionary<int, Image> _diskImagesBySize = new();
    private readonly Dictionary<(DriveVisualKind Kind, int Size), Image> _partitionImagesByKind = new();
    private bool _initialInventoryLoading = true;
    private bool _diskRefreshPending;
    private bool _diskRefreshInProgress;
    private Task? _diskRefreshTask;
    private int? _pendingRefreshDiskNumber;
    private int? _pendingRefreshPartitionNumber;
    private string? _pendingRefreshMountedWimDirectory;
    private bool _operationActive => _operationCoordinator.IsOperationActive;
    private IReadOnlyList<WimMountedImageInfo> _mountedWims = Array.Empty<WimMountedImageInfo>();
    private readonly Dictionary<string, PendingWimUnmountState> _pendingWimUnmounts = new(StringComparer.OrdinalIgnoreCase);
    private string _loadError = string.Empty;

    private readonly ImagingManagerLayoutMetrics _mDip = new();
    private ImagingManagerLayoutMetricsPx _mPx = null!;
    private Font? _chromeFont;
    private Font? _detailFont;
    private float _lastChromeFontPx;
    private float _lastDetailFontPx;
    private Size _lastNormalClientSize;

    public MainForm()
    {
        _driveLetterReassignment = new DriveLetterReassignmentService(_temporaryDriveLetters);
        _wimDeployment = new WimDeploymentService(_wimBackend);
        _winReStaging = new WinReStagingService(_temporaryDriveLetters);

        AutoScaleMode = AutoScaleMode.None;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = "Imaging Manager";
        Icon = ShellOwnedWindowIcons.CreateWindowIcon(ShellOwnedWindowIcons.ImagingManagerIconIndex) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        _ = Handle;
        RecalcMetrics();
        RebuildFonts();
        ClientSize = new Size(_mPx.InitialClientWidth, _mPx.InitialClientHeight);
        TrackNormalClientSize();

        InitializeDiskUi();
        ApplyMinimumSize();
        PerformLayout();
        ApplyLayoutMetrics();
        UpdateSelectedDiskPanel();
        CenterInitialWindow();
        TrackNormalClientSize();
        LoadPendingWimUnmountState();

        SynchronizationContext uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(uiContext);

        _storageChangeCoordinator = new StorageChangeCoordinator(uiContext, monitorBitLocker: true);
        _storageChangeCoordinator.StorageChanged += StorageChangeCoordinator_StorageChanged;

        _ = InitializeInventoryAsync();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TrackNormalClientSize();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        TrackNormalClientSize();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        DpiWindowResizeState? resizeState = CreateDpiWindowResizeState(e);
        base.OnDpiChanged(e);
        ReapplyDpiMetrics(resizeState);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopActivationListener();

            if (_storageChangeCoordinator is not null)
            {
                _storageChangeCoordinator.StorageChanged -= StorageChangeCoordinator_StorageChanged;
                _storageChangeCoordinator.Dispose();
                _storageChangeCoordinator = null;
            }

            foreach (Image image in _diskImagesBySize.Values.Concat(_partitionImagesByKind.Values).Distinct())
                image.Dispose();

            _diskImagesBySize.Clear();
            _partitionImagesByKind.Clear();
            _chromeFont?.Dispose();
            _detailFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task InitializeInventoryAsync()
    {
        try
        {
            await RequestDiskRefreshAsync();
            await RefreshMountedWimStateAsync();
        }
        finally
        {
            _initialInventoryLoading = false;
            if (!IsDisposed && !Disposing)
            {
                _storageChangeCoordinator?.Start();
                UpdateSelectedDiskPanel();
            }
        }
    }

    private void StorageChangeCoordinator_StorageChanged(object? sender, StorageChangeEventArgs e)
    {
        _ = RequestDiskRefreshAsync();
    }

    private bool TryBeginOperation(string operationName, ImagingDiskInfo? disk = null)
    {
        bool started = _operationCoordinator.TryBegin(operationName, disk);
        if (started)
            RefreshDiskOperationIndicators();
        return started;
    }

    private void EndOperation()
    {
        _operationCoordinator.End();
        RefreshDiskOperationIndicators();
    }

    private void SetWaitCursorState(bool waiting)
    {
        if (IsDisposed || Disposing)
            return;

        Cursor target = waiting ? Cursors.WaitCursor : Cursors.Default;
        UseWaitCursor = waiting;
        Cursor = target;
        Cursor.Current = target;
    }

}
