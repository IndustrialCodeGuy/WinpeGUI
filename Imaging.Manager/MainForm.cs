using Imaging.Core;
using Shared.Shell.Models;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;

namespace Imaging.Manager;

public partial class MainForm : Form
{
    private readonly DiskInventory _inventory = new();
    private readonly DismFfuBackend _ffuBackend = new();
    private readonly DismWimBackend _wimBackend = new();
    private readonly TemporaryDriveLetterService _temporaryDriveLetters = new();
    private readonly WimDeploymentService _wimDeployment;
    private readonly WinReStagingService _winReStaging;
    private readonly PartitionFormatService _partitionFormatter = new();

    private FlowLayoutPanel _pnlDisks = null!;
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
    private bool _isLoading;
    private bool _operationActive;
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
        LoadPendingWimUnmountState();
        LoadDisks();
        _ = RefreshMountedWimStateAsync();
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
            foreach (Image image in _diskImagesBySize.Values.Concat(_partitionImagesByKind.Values).Distinct())
                image.Dispose();

            _diskImagesBySize.Clear();
            _partitionImagesByKind.Clear();
            _chromeFont?.Dispose();
            _detailFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
