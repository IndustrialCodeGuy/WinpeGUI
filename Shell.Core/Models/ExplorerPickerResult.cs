namespace Shell.Core.Models;

public sealed class ExplorerPickerResult
{
    public bool Accepted { get; init; }
    public string? SelectedPath { get; init; }
    public string? ErrorMessage { get; init; }

    public static ExplorerPickerResult Accept(string selectedPath)
    {
        return new ExplorerPickerResult
        {
            Accepted = true,
            SelectedPath = selectedPath
        };
    }

    public static ExplorerPickerResult Cancel()
    {
        return new ExplorerPickerResult();
    }

    public static ExplorerPickerResult Error(string errorMessage)
    {
        return new ExplorerPickerResult
        {
            ErrorMessage = errorMessage
        };
    }
}
