using System.Management;
using System.Security.Principal;

namespace Shell.Infrastructure.DriveState;

public enum BitLockerIntegrationState
{
    Available,
    NotElevated,
    ProviderUnavailable
}

public sealed class BitLockerRuntimeCapabilities
{
    public BitLockerIntegrationState State { get; init; }

    public bool IsAvailable => State == BitLockerIntegrationState.Available;
    public bool CanReadStatus => IsAvailable;
    public bool CanUseExplorerBitLockerUi => IsAvailable;
    public bool CanShowBitLockerManagerStartMenu => State != BitLockerIntegrationState.ProviderUnavailable;

    public static BitLockerRuntimeCapabilities Detect()
    {
        if (!IsProcessElevated())
        {
            return new BitLockerRuntimeCapabilities
            {
                State = BitLockerIntegrationState.NotElevated
            };
        }

        return new BitLockerRuntimeCapabilities
        {
            State = IsBitLockerWmiAvailable()
                ? BitLockerIntegrationState.Available
                : BitLockerIntegrationState.ProviderUnavailable
        };
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBitLockerWmiAvailable()
    {
        try
        {
            ConnectionOptions options = new()
            {
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy
            };

            ManagementScope scope = new(
                @"\\.\Root\CIMV2\Security\MicrosoftVolumeEncryption",
                options);

            scope.Connect();

            using ManagementClass volumeClass = new(
                scope,
                new ManagementPath("Win32_EncryptableVolume"),
                null);

            volumeClass.Get();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
