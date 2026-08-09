namespace Shell.Infrastructure.DriveState;

public sealed class DriveStatesChangedEventArgs : EventArgs
{
    public DriveStatesChangedEventArgs(IEnumerable<string>? affectedDriveRoots)
    {
        AffectedDriveRoots = (affectedDriveRoots ?? Array.Empty<string>())
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> AffectedDriveRoots { get; }
}
