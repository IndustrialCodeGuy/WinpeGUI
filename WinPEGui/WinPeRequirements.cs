using System.Management;

namespace WinPEGui;

internal sealed record WinPeRequirementFailure(string Component, string Detail);

internal static class WinPeRequirements
{
    public static IReadOnlyList<WinPeRequirementFailure> Validate()
    {
        Dictionary<string, List<string>> failures = new(StringComparer.OrdinalIgnoreCase);

        CheckWmiClass(
            failures,
            "WinPE-WMI",
            @"root\CIMV2",
            "Win32_OperatingSystem");

        CheckFile(
            failures,
            "WinPE-SecureStartup",
            Path.Combine(Environment.SystemDirectory, "manage-bde.exe"),
            "manage-bde.exe");

        CheckWmiClass(
            failures,
            "WinPE-SecureStartup",
            @"root\CIMV2\Security\MicrosoftVolumeEncryption",
            "Win32_EncryptableVolume");

        CheckFile(
            failures,
            "WinPE-PowerShell",
            Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            "powershell.exe");

        CheckWmiClass(
            failures,
            "WinPE-StorageWMI",
            @"root\Microsoft\Windows\Storage",
            "MSFT_Disk");

        CheckWmiClass(
            failures,
            "WinPE-StorageWMI",
            @"root\Microsoft\Windows\Storage",
            "MSFT_Partition");

        return failures
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new WinPeRequirementFailure(
                pair.Key,
                string.Join("; ", pair.Value.Distinct(StringComparer.OrdinalIgnoreCase))))
            .ToArray();
    }

    public static string BuildFailureMessage(IReadOnlyList<WinPeRequirementFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        string details = string.Join(
            Environment.NewLine,
            failures.Select(static failure => $"  - {failure.Component}: {failure.Detail}"));

        return
            "WinPE GUI cannot start because this WinPE image does not meet the required component profile." +
            Environment.NewLine + Environment.NewLine +
            "Missing or unavailable:" + Environment.NewLine +
            details +
            Environment.NewLine + Environment.NewLine +
            "Required WinPE optional components:" + Environment.NewLine +
            "  WinPE-WMI" + Environment.NewLine +
            "  WinPE-NetFX" + Environment.NewLine +
            "  WinPE-Scripting" + Environment.NewLine +
            "  WinPE-PowerShell" + Environment.NewLine +
            "  WinPE-StorageWMI" + Environment.NewLine +
            "  WinPE-SecureStartup" +
            Environment.NewLine + Environment.NewLine +
            "See README.md for the supported WinPE image configuration.";
    }

    private static void CheckFile(
        IDictionary<string, List<string>> failures,
        string component,
        string path,
        string displayName)
    {
        if (!File.Exists(path))
            AddFailure(failures, component, $"{displayName} was not found at {path}");
    }

    private static void CheckWmiClass(
        IDictionary<string, List<string>> failures,
        string component,
        string scopePath,
        string className)
    {
        try
        {
            ManagementScope scope = new(scopePath);
            scope.Connect();

            using ManagementClass managementClass = new(
                scope,
                new ManagementPath(className),
                null);

            managementClass.Get();
        }
        catch (Exception ex)
        {
            AddFailure(failures, component, $"{scopePath}\\{className}: {ex.Message}");
        }
    }

    private static void AddFailure(
        IDictionary<string, List<string>> failures,
        string component,
        string detail)
    {
        if (!failures.TryGetValue(component, out List<string>? componentFailures))
        {
            componentFailures = new List<string>();
            failures.Add(component, componentFailures);
        }

        componentFailures.Add(detail);
    }
}
