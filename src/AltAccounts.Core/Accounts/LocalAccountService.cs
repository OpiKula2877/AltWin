using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using Microsoft.Win32;

namespace AltAccounts.Core.Accounts;

/// <summary>Reads local accounts. Works without admin rights.</summary>
public static class LocalAccountService
{
    public static string CurrentUserSid
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return id.User?.Value ?? "";
        }
    }

    public static string CurrentUserName => Environment.UserName;

    public static bool IsCurrentProcessElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>All local accounts (including disabled), with type and profile path.</summary>
    public static List<AccountInfo> GetAll()
    {
        var result = new List<AccountInfo>();
        using var ctx = new PrincipalContext(ContextType.Machine);
        var adminSids = GroupMemberSids(ctx, AccountFilter.AdministratorsSid);

        using var searcher = new PrincipalSearcher(new UserPrincipal(ctx));
        foreach (var p in searcher.FindAll())
        {
            using (p)
            {
                if (p is not UserPrincipal u || u.Sid is null) continue;
                string sid = u.Sid.Value;
                result.Add(new AccountInfo(
                    sid,
                    u.SamAccountName ?? u.Name ?? "",
                    u.DisplayName ?? "",
                    adminSids.Contains(sid) ? AccountType.Admin : AccountType.Standard,
                    u.Enabled ?? true,
                    GetProfilePath(sid),
                    GetPicturePath(sid)));
            }
        }
        return result.OrderBy(a => a.ShownName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<AccountInfo> GetListable() => GetAll().Where(AccountFilter.IsListable).ToList();

    internal static HashSet<string> GroupMemberSids(PrincipalContext ctx, string groupSid)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var group = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, groupSid);
        if (group is null) return set;
        try
        {
            foreach (var m in group.GetMembers())
                using (m)
                    if (m.Sid is not null) set.Add(m.Sid.Value);
        }
        catch (Exception ex) when (ex is PrincipalOperationException or NoMatchingPrincipalException)
        {
            // GetMembers can fail on orphaned (unresolvable) members; read the raw SIDs instead.
            if (group.GetUnderlyingObject() is System.DirectoryServices.DirectoryEntry de &&
                de.Invoke("Members") is System.Collections.IEnumerable members)
            {
                foreach (var member in members)
                {
                    using var e = new System.DirectoryServices.DirectoryEntry(member);
                    if (e.Properties["objectSid"].Value is byte[] bytes)
                        set.Add(new SecurityIdentifier(bytes, 0).Value);
                }
            }
        }
        return set;
    }

    public static string? GetProfilePath(string sid)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") is string s ? Environment.ExpandEnvironmentVariables(s) : null;
    }

    /// <summary>Best effort: the account picture Windows stores for the user, if readable.</summary>
    public static string? GetPicturePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\{sid}");
            foreach (var name in new[] { "Image192", "Image208", "Image240", "Image96", "Image448" })
                if (key?.GetValue(name) is string path && File.Exists(path)) return path;
        }
        catch (Exception) { /* not readable for this user: fall back to initials */ }
        return null;
    }

    public static string? UserNameToSid(string userName)
    {
        try { return ((SecurityIdentifier)new NTAccount(Environment.MachineName, userName).Translate(typeof(SecurityIdentifier))).Value; }
        catch (Exception) { return null; }
    }

    public static bool SidExists(string sid)
    {
        try { new SecurityIdentifier(sid).Translate(typeof(NTAccount)); return true; }
        catch (Exception) { return false; }
    }
}
