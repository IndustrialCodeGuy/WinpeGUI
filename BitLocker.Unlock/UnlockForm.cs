using BitLocker.Core;
using Shared.Shell.Theming;
using Shared.Shell.Utilities;
using Shell.Core.Pickers;
using System.Runtime.InteropServices;

namespace BitLocker.Unlock;

public sealed partial class UnlockForm : Form
{
    // Launch state and backend
    private readonly string _drivePath;
    private readonly BitLockerWmiBackend _backend = new();

    // Single-instance activation support
    private EventWaitHandle? _activateEvent;
    private Thread? _activateThread;

    // UI state and controls
    private BitLockerVolumeInfo? _volume;
    private Label _lblRecoveryKeyId = null!;
    private Label _lblPrompt = null!;
    private TextBox _txtSecret = null!;
    private LinkLabel _lnkRecoveryPassword = null!;
    private LinkLabel _lnkRecoveryKeyFile = null!;
    private Button _btnUnlock = null!;
    private Button _btnCancel = null!;
    private string? _recoveryKeyIdText;

    // DPI-scaled layout and fonts
    private readonly UnlockLayoutMetrics _mDip = new();
    private UnlockLayoutMetricsPx _mPx = null!;
    private Font? _chromeFont;
    private float _lastChromeFontPx;

    private UnlockInputMode _mode = UnlockInputMode.Passphrase;
    private bool _modeInitialized;
    
    public UnlockForm(string drivePath)
    {
        _drivePath = BitLockerDrivePath.NormalizeDrivePath(drivePath) ?? drivePath;

        // Unlock chrome is manually scaled through UnlockLayoutMetrics
        AutoScaleMode = AutoScaleMode.None;
        AutoScaleDimensions = new SizeF(96f, 96f);

        Text = $"BitLocker Unlock Drive {GetDriveTitleText(_drivePath)}";
        Icon = ShellOwnedWindowIcons.CreateWindowIcon(ShellOwnedWindowIcons.BitLockerUnlockIconIndex) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ShellTheme.WindowBack;
        ForeColor = ShellTheme.TextColor;

        // Force the top-level handle before calculating DeviceDpi-based metrics
        _ = Handle;

        RecalcMetrics();
        RebuildFonts();
        ClientSize = new Size(_mPx.ClientWidth, _mPx.ClientHeight);

        InitializeUi();
        InitializeActivationSignal();
    }

    public bool Unlocked { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _volume = FindVolume(_drivePath);
        if (_volume == null)
        {
            string message = string.IsNullOrWhiteSpace(_backend.LastErrorMessage)
                ? $"BitLocker volume was not found:\n{_drivePath}"
                : _backend.LastErrorMessage;

            MessageBox.Show(
                this,
                message,
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            Close();
            return;
        }

        SetMode(UnlockInputMode.Passphrase);

        if (_volume.IsLocked != true)
        {
            Unlocked = true;
            Close();
            return;
        }

        _txtSecret.Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        Bounds = e.SuggestedRectangle;

        // Temporarily show mode-specific controls while recalculating DPI
        bool showRecoveryKeyIdForDpi = !_lblRecoveryKeyId.Visible;
        bool showRecoveryKeyFileForDpi = !_lnkRecoveryKeyFile.Visible;

        SuspendLayout();
        try
        {
            if (showRecoveryKeyIdForDpi)
                _lblRecoveryKeyId.Visible = true;

            if (showRecoveryKeyFileForDpi)
                _lnkRecoveryKeyFile.Visible = true;

            ReapplyDpiMetrics();

            if (showRecoveryKeyIdForDpi)
                _lblRecoveryKeyId.Visible = false;

            if (showRecoveryKeyFileForDpi)
                _lnkRecoveryKeyFile.Visible = false;
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activateEvent?.Dispose();
            _chromeFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    // UI construction

    private void InitializeUi()
    {
        _lblRecoveryKeyId = new Label
        {
            Left = _mPx.Margin,
            Top = _mPx.RecoveryKeyIdTop,
            Width = _mPx.RecoveryKeyIdWidth,
            Height = _mPx.RecoveryKeyIdHeight,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = false
        };

        _lblPrompt = new Label
        {
            Left = _mPx.Margin,
            Top = _mPx.PromptTop,
            Width = _mPx.ContentWidth,
            Height = _mPx.PromptHeight,
            Text = "Enter password to unlock this drive:"
        };

        _txtSecret = new TextBox
        {
            Left = _mPx.Margin,
            Top = _mPx.SecretTop,
            Width = _mPx.ContentWidth,
            PasswordChar = '*',
            UseSystemPasswordChar = true
        };

        _lnkRecoveryPassword = new LinkLabel
        {
            Left = _mPx.Margin,
            Top = _mPx.LinkTop,
            Width = _mPx.RecoveryPasswordLinkWidth,
            Height = _mPx.LinkHeight,
            Text = "Use recovery password"
        };
        _lnkRecoveryPassword.LinkClicked += (_, _) =>
        {
            SetMode(_mode == UnlockInputMode.RecoveryPassword
                ? UnlockInputMode.Passphrase
                : UnlockInputMode.RecoveryPassword);
        };

        _lnkRecoveryKeyFile = new LinkLabel
        {
            Left = _mPx.RecoveryKeyFileLinkLeft,
            Top = _mPx.LinkTop,
            Width = _mPx.RecoveryKeyFileLinkWidth,
            Height = _mPx.LinkHeight,
            Text = "Use recovery key file",
            Visible = false
        };
        _lnkRecoveryKeyFile.LinkClicked += (_, _) => UnlockWithRecoveryKeyFile();

        _btnUnlock = new Button
        {
            Left = _mPx.UnlockButtonLeft,
            Top = _mPx.ButtonTop,
            Width = _mPx.ButtonWidth,
            Height = _mPx.ButtonHeight,
            Text = "Unlock",
            UseVisualStyleBackColor = true
        };
        _btnUnlock.Click += (_, _) => UnlockWithCurrentInput();

        _btnCancel = new Button
        {
            Left = _mPx.CancelButtonLeft,
            Top = _mPx.ButtonTop,
            Width = _mPx.ButtonWidth,
            Height = _mPx.ButtonHeight,
            Text = "Cancel",
            UseVisualStyleBackColor = true,
            DialogResult = DialogResult.Cancel
        };
        _btnCancel.Click += (_, _) => Close();

        AcceptButton = _btnUnlock;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblRecoveryKeyId,
            _lblPrompt,
            _txtSecret,
            _lnkRecoveryPassword,
            _lnkRecoveryKeyFile,
            _btnUnlock,
            _btnCancel
        });

        ApplyChromeFonts();
        LayoutUnlockControls();
    }

    // Volume and input-mode state

    private BitLockerVolumeInfo? FindVolume(string drivePath)
    {
        string normalized = BitLockerDrivePath.NormalizeDrivePath(drivePath) ?? drivePath;
        return _backend.GetVolume(normalized);
    }

    private static string GetDriveTitleText(string drivePath)
    {
        string normalized = BitLockerDrivePath.NormalizeDrivePath(drivePath) ?? drivePath;

        if (normalized.Length >= 2 && normalized[1] == ':')
            return normalized[..2];

        return normalized.TrimEnd('\\');
    }

    private void SetMode(UnlockInputMode mode)
    {
        if (_modeInitialized && _mode == mode)
        {
            if (!_txtSecret.Focused)
                _txtSecret.Focus();

            return;
        }

        _mode = mode;
        _modeInitialized = true;

        SuspendLayout();
        try
        {
            if (_txtSecret.TextLength > 0)
                _txtSecret.Clear();

            switch (mode)
            {
                case UnlockInputMode.Passphrase:
                    SetVisibleIfChanged(_lblRecoveryKeyId, false);
                    SetTextIfChanged(_lblPrompt, "Enter password to unlock this drive:");

                    if (!_txtSecret.UseSystemPasswordChar)
                        _txtSecret.UseSystemPasswordChar = true;

                    if (_txtSecret.PasswordChar != '*')
                        _txtSecret.PasswordChar = '*';

                    SetTextIfChanged(_lnkRecoveryPassword, "Use recovery password");
                    SetVisibleIfChanged(_lnkRecoveryKeyFile, false);
                    break;

                case UnlockInputMode.RecoveryPassword:
                    SetTextIfChanged(_lblRecoveryKeyId, GetRecoveryKeyIdText());
                    SetVisibleIfChanged(_lblRecoveryKeyId, true);
                    SetTextIfChanged(_lblPrompt, "Enter recovery password to unlock this drive:");

                    if (_txtSecret.UseSystemPasswordChar)
                        _txtSecret.UseSystemPasswordChar = false;

                    if (_txtSecret.PasswordChar != '\0')
                        _txtSecret.PasswordChar = '\0';

                    SetTextIfChanged(_lnkRecoveryPassword, "Use password");
                    SetVisibleIfChanged(_lnkRecoveryKeyFile, true);
                    break;
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        if (!_txtSecret.Focused)
            _txtSecret.Focus();
    }

    private string GetRecoveryKeyIdText()
    {
        if (_recoveryKeyIdText != null)
            return _recoveryKeyIdText;

        if (_volume == null)
            return "Recovery Key ID:";

        string keyIdPrefix = _backend.GetRecoveryKeyIdPrefix(_volume.MountPoint);

        _recoveryKeyIdText = string.IsNullOrWhiteSpace(keyIdPrefix)
            ? "Recovery Key ID: Not found"
            : $"Recovery Key ID: {keyIdPrefix}";

        return _recoveryKeyIdText;
    }

    private void UnlockWithCurrentInput()
    {
        if (_volume == null)
            return;

        char[] secret = _txtSecret.Text.ToCharArray();
        try
        {
            BitLockerOperationResult result = _mode == UnlockInputMode.RecoveryPassword
                ? _backend.UnlockWithRecoveryPassword(_volume.MountPoint, secret)
                : _backend.UnlockWithPassphrase(_volume.MountPoint, secret);

            HandleUnlockResult(result);
        }
        finally
        {
            Array.Clear(secret, 0, secret.Length);
        }
    }

    private void UnlockWithRecoveryKeyFile()
    {
        if (_volume == null)
            return;

        string? keyFilePath = ExplorerPickerClient.PickOpenFile(
            initialPath: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            title: "Select BitLocker Recovery Key",
            allowedExtensions: new[] { ".bek" },
            ownerWindowHandle: Handle);

        if (string.IsNullOrWhiteSpace(keyFilePath))
            return;

        BitLockerOperationResult unlockResult = _backend.UnlockWithRecoveryKeyFile(
            _volume.MountPoint,
            keyFilePath);

        HandleUnlockResult(unlockResult);
    }

    private void HandleUnlockResult(BitLockerOperationResult result)
    {
        if (result.Success)
        {
            Unlocked = true;
            Close();
            return;
        }

        string message = result.Message;
        if (string.IsNullOrWhiteSpace(message))
            message = $"The drive could not be unlocked. Return code: {result.ReturnCode}";

        MessageBox.Show(
            this,
            message,
            "Unlock Drive",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    // Single-instance activation

    private void InitializeActivationSignal()
    {
        try
        {
            _activateEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                BitLockerUnlockActivation.GetUnlockActivateEventName(_drivePath));

            _activateThread = new Thread(() =>
            {
                while (!IsDisposed)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                        PostUnlockActivation();
                    }
                    catch
                    {
                        return;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "BitLocker Unlock Activation Listener"
            };

            _activateThread.Start();
        }
        catch
        {
        }
    }

    private void PostUnlockActivation()
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke((Action)BringUnlockWindowForward);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void BringUnlockWindowForward()
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        try
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            FlashUnlockWindow();

            SetForegroundWindow(Handle);
            Activate();
            BringToFront();
            _txtSecret.Focus();
        }
        catch
        {
        }
    }

    // Native foreground/flash helpers

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_CAPTION = 0x00000001;
    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_ALL = FLASHW_CAPTION | FLASHW_TRAY;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void FlashUnlockWindow()
    {
        if (!IsHandleCreated)
            return;

        FLASHWINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = Handle,
            dwFlags = FLASHW_ALL,
            uCount = 6,
            dwTimeout = 0
        };

        FlashWindowEx(ref info);
    }

    private enum UnlockInputMode
    {
        Passphrase,
        RecoveryPassword
    }
}
