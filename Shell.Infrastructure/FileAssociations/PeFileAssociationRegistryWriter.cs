using Microsoft.Win32;
using Shared.Shell.Utilities;
using Shell.Infrastructure.FileTypes;
using System.Runtime.InteropServices;

namespace Shell.Infrastructure.FileAssociations;

public static class PeFileAssociationRegistryWriter
{
    // In WinPE, WinPeShell intentionally replaces the default extension-to-ProgID
    // mappings for the curated built-in file types so that shell consumers
    // (Properties, default open, icons, etc.) align with WinPeShell's internal
    // built-in association policy.
    //
    // Original PE ProgID keys are not deleted, but the extension default is
    // redirected to the WinPeShell ProgID.

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    private static IEnumerable<BuiltInFileTypeDefinition> Registrations =>
        ExplorerBuiltInFileAssociations.GetDefinitions().Where(static definition => definition.RegisterInPe);

    public static void ApplyIfWinPE()
    {
        if (!PlatformDetect.IsWinPE)
            return;

        try
        {
            Apply();
            NotifyAssociationChanged();
        }
        catch
        {
            // Registry association registration is best-effort.
            // WinPeShell's internal association service remains authoritative.
        }
    }

    private static void Apply()
    {
        foreach (BuiltInFileTypeDefinition registration in Registrations)
        {
            WriteExtension(registration);
            WriteProgId(registration);
        }
    }

    // In WinPE, WinPeShell intentionally replaces the extension-to-ProgID
    // mappings for curated built-in file types. This makes shell consumers
    // such as Properties, default Open, and icon/type queries align with
    // WinPeShell's built-in safe association policy.
    //
    // Original PE ProgID keys are not deleted, but the extension default is
    // redirected to the WinPeShell ProgID.
    private static void WriteExtension(BuiltInFileTypeDefinition registration)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey($@"Software\Classes\{registration.Extension}");

        key.SetValue(null, registration.RegistryProgId!, RegistryValueKind.String);

        SetOrDelete(key, "PerceivedType", registration.PerceivedType, RegistryValueKind.String);
        SetOrDelete(key, "Content Type", registration.ContentType, RegistryValueKind.String);
    }

    private static void WriteProgId(BuiltInFileTypeDefinition registration)
    {
        using RegistryKey progIdKey = Registry.LocalMachine.CreateSubKey($@"Software\Classes\{registration.RegistryProgId!}");

        progIdKey.SetValue(null, registration.RegistryDisplayName, RegistryValueKind.String);

        using (RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon"))
        {
            iconKey.SetValue(null, registration.RegistryDefaultIcon!, GetRegistryValueKind(registration.RegistryDefaultIcon!));
        }

        if (!string.IsNullOrWhiteSpace(registration.RegistryOpenCommand))
        {
            using RegistryKey commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(null, registration.RegistryOpenCommand, GetRegistryValueKind(registration.RegistryOpenCommand));
        }
        else
        {
            try
            {
                progIdKey.DeleteSubKeyTree(@"shell\open", throwOnMissingSubKey: false);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static void SetOrDelete(RegistryKey key, string name, string? value, RegistryValueKind kind)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            try
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
            catch
            {
                // Ignore cleanup failures.
            }

            return;
        }

        key.SetValue(name, value, kind);
    }

    private static RegistryValueKind GetRegistryValueKind(string value)
    {
        return value.Contains('%', StringComparison.Ordinal)
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;
    }

    private static void NotifyAssociationChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);
}
