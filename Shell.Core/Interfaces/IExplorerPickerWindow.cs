namespace Shell.Core.Interfaces;

public interface IExplorerPickerWindow : IExplorerWindow
{
    string? SelectedPath { get; }
}
