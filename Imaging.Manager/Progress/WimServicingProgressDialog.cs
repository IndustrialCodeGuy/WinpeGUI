using Imaging.Core;

namespace Imaging.Manager;

internal sealed class WimServicingProgressDialog : ImagingProgressDialogBase
{
    public WimServicingProgressDialog(string titleText, string heading, string detail)
        : base(
            titleText,
            heading,
            "Preparing image servicing...",
            detail)
    {
    }

    public void BeginPhase(
        string heading,
        string detail,
        string status = "Preparing image operation...") =>
        SetPhase(heading, detail, status);

    public void UpdateProgress(WimOperationProgress progress) =>
        ApplyProgressUpdate(progress.Percentage, progress.Message);
}
