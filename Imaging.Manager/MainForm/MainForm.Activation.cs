namespace Imaging.Manager;

public partial class MainForm
{
    internal void StartActivationListener(EventWaitHandle activateEvent)
    {
        if (_activationTask != null)
            return;

        _activationCts = new CancellationTokenSource();
        CancellationToken token = _activationCts.Token;
        _activationTask = Task.Run(() => WaitForActivationRequests(activateEvent, token));
    }

    private void WaitForActivationRequests(EventWaitHandle activateEvent, CancellationToken token)
    {
        WaitHandle[] waitHandles = [activateEvent, token.WaitHandle];

        while (true)
        {
            int signaledIndex;
            try
            {
                signaledIndex = WaitHandle.WaitAny(waitHandles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signaledIndex != 0 || token.IsCancellationRequested)
                return;

            try
            {
                BeginInvoke((Action)ActivateFromSecondaryInstance);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }

    private void ActivateFromSecondaryInstance()
    {
        if (IsDisposed)
            return;

        if (!Visible)
            Show();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        Activate();
        BringToFront();
    }

    private void StopActivationListener()
    {
        try
        {
            _activationCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _activationTask?.Wait(250);
        }
        catch
        {
        }

        _activationTask = null;
        _activationCts?.Dispose();
        _activationCts = null;
    }
}
