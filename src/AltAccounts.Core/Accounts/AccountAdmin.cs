using System.DirectoryServices.AccountManagement;
using System.Management;

namespace AltAccounts.Core.Accounts;

/// <summary>
/// Account changes. Runs only in the elevated helper. Uses documented APIs
/// (System.DirectoryServices.AccountManagement and WMI Win32_UserProfile).
/// The initial password goes straight to Windows and is never stored or logged.
/// </summary>
public static class AccountAdmin
{
    public static string CreateUser(string userName, string displayName, string password, AccountType type)
    {
        if (!AccountFilter.IsValidUserName(userName)) throw new ArgumentException("That username is not allowed by Windows.");
        using var ctx = new PrincipalContext(ContextType.Machine);
        using (var existing = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, userName))
            if (existing is not null) throw new InvalidOperationException($"An account named '{userName}' already exists.");

        using var user = new UserPrincipal(ctx)
        {
            SamAccountName = userName,
            Name = userName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            Enabled = true,
            PasswordNeverExpires = true,
        };
        user.SetPassword(password);
        user.Save();

        SetGroup(ctx, user, AccountFilter.UsersSid, true);
        SetGroup(ctx, user, AccountFilter.AdministratorsSid, type == AccountType.Admin);
        return user.Sid.Value;
    }

    public static void SetDisplayName(string sid, string displayName)
    {
        using var ctx = new PrincipalContext(ContextType.Machine);
        using var user = Find(ctx, sid);
        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        user.Save();
    }

    public static void SetType(string sid, AccountType type)
    {
        if (type == AccountType.Standard)
        {
            var admins = LocalAccountService.GetAll().Where(a => a.Enabled && a.Type == AccountType.Admin).ToList();
            if (admins.Count == 1 && admins[0].Sid.Equals(sid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This is the last enabled admin account. Make another account an admin first.");
        }
        using var ctx = new PrincipalContext(ContextType.Machine);
        using var user = Find(ctx, sid);
        SetGroup(ctx, user, AccountFilter.UsersSid, true);
        SetGroup(ctx, user, AccountFilter.AdministratorsSid, type == AccountType.Admin);
    }

    /// <summary>Throws if the account must not be removed (yourself, or the last admin).</summary>
    public static void EnsureCanDelete(string sid)
    {
        if (sid.Equals(LocalAccountService.CurrentUserSid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You cannot remove the account you are signed in with.");
        var all = LocalAccountService.GetAll();
        var target = all.FirstOrDefault(a => a.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Account not found.");
        if (target.Type == AccountType.Admin && all.Count(a => a.Enabled && a.Type == AccountType.Admin) <= 1)
            throw new InvalidOperationException("This is the last admin account.");
    }

    public static void DeleteUser(string sid, bool deleteProfile)
    {
        EnsureCanDelete(sid);
        using (var ctx = new PrincipalContext(ContextType.Machine))
        using (var user = Find(ctx, sid))
            user.Delete();

        if (deleteProfile) DeleteProfile(sid);
    }

    /// <summary>Deletes the profile folder and its registry entry through Win32_UserProfile.</summary>
    public static void DeleteProfile(string sid)
    {
        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) || sid.Contains('\''))
            throw new InvalidOperationException("Refusing to delete a non-user profile.");
        using var searcher = new ManagementObjectSearcher(new SelectQuery("Win32_UserProfile", $"SID = '{sid}'"));
        foreach (var obj in searcher.Get())
        {
            using var profile = (ManagementObject)obj;
            if (profile["Loaded"] is true)
                throw new InvalidOperationException("The profile is still loaded. Sign the account out first.");
            if (profile["Special"] is true)
                throw new InvalidOperationException("Refusing to delete a special system profile.");
            profile.Delete();
        }
    }

    private static UserPrincipal Find(PrincipalContext ctx, string sid) =>
        UserPrincipal.FindByIdentity(ctx, IdentityType.Sid, sid) ?? throw new InvalidOperationException("Account not found.");

    private static void SetGroup(PrincipalContext ctx, UserPrincipal user, string groupSid, bool member)
    {
        using var group = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, groupSid)
            ?? throw new InvalidOperationException($"Group {groupSid} not found.");
        bool isMember = LocalAccountService.GroupMemberSids(ctx, groupSid).Contains(user.Sid.Value);
        if (member && !isMember) { group.Members.Add(user); group.Save(); }
        else if (!member && isMember) { group.Members.Remove(user); group.Save(); }
    }
}
