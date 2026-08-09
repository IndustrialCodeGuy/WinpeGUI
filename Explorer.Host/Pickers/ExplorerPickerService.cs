using Shell.Core.Interfaces;
using Shell.Core.Models;

namespace Explorer.Host.Pickers;

internal sealed class ExplorerPickerService
{
    private readonly IExplorerWindowFactory _windowFactory;
    private readonly IExplorerWindowRegistry _windowRegistry;
    private readonly IDriveStateStore _driveStateStore;
    private readonly SynchronizationContext _uiContext;

    public ExplorerPickerService(
        IExplorerWindowFactory windowFactory,
        IExplorerWindowRegistry windowRegistry,
        IDriveStateStore driveStateStore,
        SynchronizationContext uiContext)
    {
        _windowFactory = windowFactory;
        _windowRegistry = windowRegistry;
        _driveStateStore = driveStateStore;
        _uiContext = uiContext;
    }

    public ExplorerPickerResult ShowPicker(
    ExplorerPickerRequest request,
    IWin32Window? fallbackOwner,
    CancellationToken cancellationToken)
    {
        request ??= new ExplorerPickerRequest();

        if (request.Mode == ExplorerWindowMode.Browse)
            return ExplorerPickerResult.Error("Browse mode is not a picker mode.");

        IExplorerWindow window = _windowFactory.CreateWindow(request.ToWindowOptions());
        _windowRegistry.Register(window);

        try
        {
            if (window is not Form form || window is not IExplorerPickerWindow pickerWindow)
                return ExplorerPickerResult.Error("Unable to create an Explorer picker window.");

            if (cancellationToken.IsCancellationRequested)
                return ExplorerPickerResult.Cancel();

            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() =>
            {
                _uiContext.Post(_ =>
                {
                    try
                    {
                        if (form.IsDisposed)
                            return;

                        form.DialogResult = DialogResult.Cancel;
                        form.Close();
                    }
                    catch
                    {
                    }
                }, null);
            });

            window.ApplyDriveSetSnapshot(
                _driveStateStore.GetCurrentSnapshot(),
                RefreshReason.InternalRequest);

            window.RequestRefreshCurrentView(RefreshReason.InternalRequest);

            IWin32Window? owner = CreateExternalOwner(request.OwnerWindowHandle) ?? fallbackOwner;
            form.StartPosition = owner == null
                ? FormStartPosition.CenterScreen
                : FormStartPosition.CenterParent;

            DialogResult dialogResult = owner == null
                ? form.ShowDialog()
                : form.ShowDialog(owner);

            string? selectedPath = pickerWindow.SelectedPath;
            if (dialogResult != DialogResult.OK || string.IsNullOrWhiteSpace(selectedPath))
                return ExplorerPickerResult.Cancel();

            return ExplorerPickerResult.Accept(selectedPath);
        }
        catch (Exception ex)
        {
            return ExplorerPickerResult.Error(ex.Message);
        }
        finally
        {
            _windowRegistry.Unregister(window.WindowId);

            if (window is Form form && !form.IsDisposed)
                form.Dispose();
        }
    }

    private static IWin32Window? CreateExternalOwner(long ownerWindowHandle)
    {
        if (ownerWindowHandle == 0)
            return null;

        IntPtr handle = new(ownerWindowHandle);
        return handle == IntPtr.Zero ? null : new ExternalWindowOwner(handle);
    }

    private sealed class ExternalWindowOwner : IWin32Window
    {
        public ExternalWindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
