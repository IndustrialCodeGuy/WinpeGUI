namespace BitLocker.Core;

public enum BitLockerLaunchAction
{
    None,
    Unlock,
    Manage
}

// Minimal command-line contract shared by the manager and unlock helper.
public sealed class BitLockerLaunchArgs
{
    public string? DrivePath { get; init; }
    public BitLockerLaunchAction Action { get; init; }

    public static BitLockerLaunchArgs Parse(string[] args)
    {
        string? drivePath = null;
        BitLockerLaunchAction action = BitLockerLaunchAction.None;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, "--drive", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                drivePath = BitLockerDrivePath.NormalizeDrivePath(args[++i]);
                continue;
            }

            if (string.Equals(arg, "--action", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                action = ParseAction(args[++i]);
                continue;
            }
        }

        return new BitLockerLaunchArgs
        {
            DrivePath = drivePath,
            Action = action
        };
    }

    private static BitLockerLaunchAction ParseAction(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BitLockerLaunchAction.None;

        return value.Trim().ToLowerInvariant() switch
        {
            "unlock" => BitLockerLaunchAction.Unlock,
            "manage" => BitLockerLaunchAction.Manage,
            _ => BitLockerLaunchAction.None
        };
    }
}
