namespace Shell.Infrastructure.DriveState;

internal sealed class DriveTopologyMessageWindow : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    private readonly Action<int> _onDeviceChange;

    public DriveTopologyMessageWindow(Action<int> onDeviceChange)
    {
        _onDeviceChange = onDeviceChange;

        CreateParams cp = new()
        {
            Caption = "ShellDriveTopologyMonitorWindow"
        };

        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WM_DEVICECHANGE)
            return;

        int code = m.WParam.ToInt32();

        switch (code)
        {
            case DBT_DEVICEARRIVAL:
            case DBT_DEVICEREMOVECOMPLETE:
            case DBT_DEVNODES_CHANGED:
                _onDeviceChange(code);
                break;
        }
    }

    public void Dispose()
    {
        try
        {
            DestroyHandle();
        }
        catch
        {
        }
    }
}