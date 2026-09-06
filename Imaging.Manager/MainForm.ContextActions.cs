namespace Imaging.Manager;

public partial class MainForm
{
    private sealed class ContextAction
    {
        public ContextAction(string text, Func<Task> executeAsync)
        {
            ExecuteAsync = executeAsync;
            Button = new Button
            {
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        public Button Button { get; }
        public Func<Task> ExecuteAsync { get; }

        public string Text
        {
            get => Button.Text;
            set => Button.Text = value;
        }

        public bool Visible
        {
            get => Button.Visible;
            set => Button.Visible = value;
        }

        public bool Enabled
        {
            get => Button.Enabled;
            set => Button.Enabled = value;
        }
    }

    private void InitializeContextActions()
    {
        _actionGetInfo = CreateContextAction("Get Info", () =>
        {
            ShowSelectedInfo();
            return Task.CompletedTask;
        });
        _actionCaptureFfu = CreateContextAction("Capture FFU", CaptureSelectedDiskAsync);
        _actionApplyFfu = CreateContextAction("Apply FFU", ApplyToSelectedDiskAsync);
        _actionDeployWim = CreateContextAction("Deploy WIM", DeployWimToSelectedDiskAsync);
        _actionCaptureWim = CreateContextAction("Capture WIM", CaptureSelectedPartitionWimAsync);
        _actionApplyWim = CreateContextAction("Apply WIM", ApplyWimToSelectedPartitionAsync);
        _actionUnmountWim = CreateContextAction("Unmount WIM", UnmountWimAsync);
        _actionRemountWim = CreateContextAction("Remount WIM", RemountWimAsync);
        _actionAddDrivers = CreateContextAction("Add Drivers", AddDriversAsync);
        _actionUnlock = CreateContextAction("Unlock", UnlockSelectedPartitionAsync);

        _contextActions =
        [
            _actionGetInfo,
            _actionCaptureFfu,
            _actionApplyFfu,
            _actionDeployWim,
            _actionCaptureWim,
            _actionApplyWim,
            _actionUnmountWim,
            _actionRemountWim,
            _actionAddDrivers,
            _actionUnlock
        ];
    }

    private ContextAction CreateContextAction(string text, Func<Task> executeAsync)
    {
        ContextAction action = new(text, executeAsync);
        action.Button.Click += async (_, _) => await RunContextActionAsync(action);
        return action;
    }

    private async Task RunContextActionAsync(ContextAction action) =>
        await RunUiActionAsync(action.ExecuteAsync);
}
