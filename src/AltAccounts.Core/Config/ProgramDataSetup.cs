using System.Security.AccessControl;
using System.Security.Principal;

namespace AltAccounts.Core.Config;

/// <summary>
/// Creates %ProgramData%\AltAccounts with a locked-down ACL. Elevated only.
/// Admins and SYSTEM: full control. Users: read. audit.log: users may also append (not rewrite).
/// </summary>
public static class ProgramDataSetup
{
    private static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static void EnsureSecured()
    {
        var dir = Directory.CreateDirectory(AppPaths.ProgramDataDir);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        var ds = new DirectorySecurity();
        ds.SetOwner(Admins);
        ds.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ds.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(System, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        ds.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        dir.SetAccessControl(ds);

        if (!File.Exists(AppPaths.AuditPath)) File.WriteAllText(AppPaths.AuditPath, "");
        var file = new FileInfo(AppPaths.AuditPath);
        var fs = file.GetAccessControl();
        fs.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.AppendData | FileSystemRights.Synchronize, AccessControlType.Allow));
        file.SetAccessControl(fs);
    }

    public static bool Exists => Directory.Exists(AppPaths.ProgramDataDir);
}
