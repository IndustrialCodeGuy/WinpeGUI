using System.Security.Principal;

namespace BitLocker.Core;

// BitLocker operations require elevation. Keep the check centralized so the
// manager and unlock helper fail consistently before opening any UI.
public static class AdministratorCheck
{
    public static bool IsRunningAsAdministrator()
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
}
