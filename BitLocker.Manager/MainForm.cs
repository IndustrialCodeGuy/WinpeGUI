using BitLocker.Core;
using Shared.Shell.Utilities;

namespace BitLocker.Manager;

public partial class MainForm : Form
{
    // Launch state and backend
    private readonly BitLockerLaunchArgs _launchArgs;
    private readonly IBitLockerBackend _backend;

    // Main layout controls
    private SplitContainer _splitMain = null!;
    private FlowLayoutPanel _pnlVolumes = null!;
    private Panel _rightPanel = null!;
    private readonly Dictionary<(Shared.Shell.Models.DriveVisualKind Kind, int Size), Image> _driveImagesByKind = new();
    private Panel? _selectedVolumeTile;

    // DPI-scaled layout and fonts
    private readonly BitLockerManagerLayoutMetrics _mDip = new();
    private BitLockerManagerLayoutMetricsPx _mPx = null!;
    private Font? _chromeFont;
    private Font? _statusFont;
    private float _lastChromeFontPx;
    private float _lastStatusFontPx;
    private Size _lastNormalClientSize;

    // Detail pane controls
    private Label _txtVolumeStatus = null!;
    private Label _lblStatus = null!;

    private Button _btnUnlock = null!;
    private Button _btnLock = null!;
    private Button _btnRefresh = null!;

    // Single-instance activation
    private CancellationTokenSource? _activationCts;
    private Task? _activationTask;

    // Current status snapshot
    private IReadOnlyList<BitLockerVolumeInfo> _volumes = Array.Empty<BitLockerVolumeInfo>();
    private string _volumeLoadError = string.Empty;
    private bool _isLoadingVolumes;

    internal MainForm(BitLockerLaunchArgs launchArgs)
    {
        _launchArgs = launchArgs;
        _backend = new BitLockerCompositeBackend();

        // Match the taskbar/explorer scaling model. BitLocker chrome is manually
        // scaled through BitLockerManagerLayoutMetrics, so prevent WinForms from
        // applying a second autoscale pass to the child controls.
        AutoScaleMode = AutoScaleMode.None;
        AutoScaleDimensions = new SizeF(96f, 96f);

        Text = "Manage BitLocker";
        Icon = ShellOwnedWindowIcons.CreateWindowIcon(ShellOwnedWindowIcons.BitLockerManagerIconIndex) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Shared.Shell.Theming.ShellTheme.WindowBack;
        ForeColor = Shared.Shell.Theming.ShellTheme.TextColor;

        // Force the top-level handle before calculating DeviceDpi-based metrics
        // so startup above 100% uses the actual monitor DPI.
        _ = Handle;

        RecalcMetrics();
        RebuildFonts();

        ClientSize = new Size(_mPx.InitialClientWidth, _mPx.InitialClientHeight);
        TrackNormalClientSize();

        InitializeVolumeUi();
        ApplyBitLockerMinimumSize();
        LoadVolumes(selectLaunchDrive: true);
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
            foreach (Image image in _driveImagesByKind.Values.Distinct())
                image.Dispose();

            _driveImagesByKind.Clear();

            StopActivationListener();

            _chromeFont?.Dispose();
            _statusFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
