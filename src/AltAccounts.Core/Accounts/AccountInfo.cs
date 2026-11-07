namespace AltAccounts.Core.Accounts;

public enum AccountType { Standard, Admin }

public sealed record AccountInfo(
    string Sid,
    string UserName,
    string DisplayName,
    AccountType Type,
    bool Enabled,
    string? ProfilePath,
    string? PicturePath)
{
    public string ShownName => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;
    public string TypeLabel => Type == AccountType.Admin ? "Admin" : "Standard";

    public string Initials
    {
        get
        {
            var parts = ShownName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            return ShownName.Length > 0 ? char.ToUpperInvariant(ShownName[0]).ToString() : "?";
        }
    }
}

public static class AccountFilter
{
    // Built-in accounts that never belong in the popup, enabled or not:
    // 501 Guest, 503 DefaultAccount, 504 WDAGUtilityAccount.
    private static readonly int[] AlwaysHiddenRids = [501, 503, 504];

    public const string AdministratorsSid = "S-1-5-32-544";
    public const string UsersSid = "S-1-5-32-545";

    public static int Rid(string sid)
    {
        int i = sid.LastIndexOf('-');
        return i >= 0 && int.TryParse(sid[(i + 1)..], out var rid) ? rid : -1;
    }

    /// <summary>Enabled, non-hidden local accounts. The built-in Administrator (500) shows only if someone enabled it.</summary>
    public static bool IsListable(AccountInfo a) => a.Enabled && !AlwaysHiddenRids.Contains(Rid(a.Sid));

    /// <summary>A valid local username: 1–20 chars, none of the characters Windows forbids.</summary>
    public static bool IsValidUserName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 20) return false;
        if (name.Trim() != name || name.EndsWith('.')) return false;
        const string forbidden = "\"/\\[]:;|=,+*?<>@";
        return !name.Any(c => forbidden.Contains(c) || char.IsControl(c));
    }
}
