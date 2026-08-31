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
    private readonly WinReStagingService _winReStaging = new();
    private readonly TemporaryDriveLetterService _temporaryDriveLetters = new();

    private SplitContainer _splitMain = null!;
    private FlowLayoutPanel _pnlDisks = null!;
    private Panel _rightPanel = null!;
    private FlowLayoutPanel _pnlPartitions = null!;
    private Label _txtDiskStatus = null!;
    private Label _lblStatus = null!;
    private Button _btnCapture = null!;
    private Button _btnApply = null!;
    private Button _btnRefresh = null!;
    private Button _btnCaptureWim = null!;
    private Button _btnApplyWim = null!;
    private Button _btnUnlock = null!;

    private Panel? _selectedDiskTile;
    private Panel? _selectedPartitionTile;
    private IReadOnlyList<ImagingDiskInfo> _disks = Array.Empty<ImagingDiskInfo>();
    private readonly Dictionary<int, Image> _diskImagesBySize = new();
    private readonly Dictionary<(DriveVisualKind Kind, int Size), Image> _partitionImagesByKind = new();
    private bool _isLoading;
    private bool _operationActive;
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
        AutoScaleMode = AutoScaleMode.None;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Text = "Imaging Manager";
        Icon = ShellOwnedWindowIcons.CreateWindowIcon(30) ?? Icon;
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
        LoadDisks();
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
