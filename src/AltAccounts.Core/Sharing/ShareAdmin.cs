using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AltAccounts.Core.Sharing;

/// <summary>Applies and removes folder grants with icacls. Runs in the elevated helper.</summary>
public static class ShareAdmin
{
    /// <summary>Does the account already have an explicit (non-inherited) allow entry on the folder?</summary>
    public static bool HasExplicitGrant(string path, string sid)
    {
        var acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow &&
                string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static void Grant(string path, string sid, ShareLevel level) => RunIcacls(IcaclsArgs.Grant(path, sid, level));

    public static void Remove(string path, string sid) => RunIcacls(IcaclsArgs.Remove(path, sid));

    private static void RunIcacls(string[] args)
    {
        var psi = new ProcessStartInfo("icacls.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start icacls.");
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"icacls failed ({p.ExitCode}): {output.Trim()}");
    }
}
