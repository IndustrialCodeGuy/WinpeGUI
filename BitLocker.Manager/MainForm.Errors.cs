using BitLocker.Core;

namespace BitLocker.Manager;

public partial class MainForm
{
    private void ShowOperationError(string caption, string message)
    {
        ShowOperationError(this, caption, message);
    }

    private void ShowOperationError(IWin32Window owner, string caption, BitLockerOperationResult result)
    {
        string message = result.Message;
        if (string.IsNullOrWhiteSpace(message))
            message = $"Operation failed. Return code: {result.ReturnCode}";

        ShowOperationError(owner, caption, message);
    }

    private static void ShowOperationError(IWin32Window owner, string caption, string message)
    {
        MessageBox.Show(
            owner,
            message,
            caption,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
