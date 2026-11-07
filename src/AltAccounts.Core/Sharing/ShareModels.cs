using System.Text.Json.Serialization;

namespace AltAccounts.Core.Sharing;

[JsonConverter(typeof(JsonStringEnumConverter<ShareLevel>))]
public enum ShareLevel { Read, Modify, Full }

public sealed class ShareGrant
{
    public string Sid { get; set; } = "";
    public string UserName { get; set; } = "";
    public ShareLevel Level { get; set; }
    /// <summary>True when the account already had its own explicit permission before the app granted one.
    /// Then the app never removes anything for this account (it cannot tell its grant apart).</summary>
    public bool PreExisting { get; set; }
    public DateTime GrantedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ShareEntry
{
    public string Path { get; set; } = "";
    public List<ShareGrant> Grants { get; set; } = [];
    /// <summary>Idea 15: warn when the folder grows past this size.</summary>
    public double? WarnAtGb { get; set; }
}

public static class IcaclsArgs
{
    public static string Rights(ShareLevel level) => level switch
    {
        ShareLevel.Read => "R",
        ShareLevel.Modify => "M",
        ShareLevel.Full => "F",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>icacls "path" /grant *SID:(OI)(CI)R|M|F  — (OI)(CI) applies to subfolders and files.</summary>
    public static string[] Grant(string path, string sid, ShareLevel level)
    {
        Validate(sid);
        return [path, "/grant", $"*{sid}:(OI)(CI){Rights(level)}"];
    }

    /// <summary>icacls "path" /remove:g *SID</summary>
    public static string[] Remove(string path, string sid)
    {
        Validate(sid);
        return [path, "/remove:g", $"*{sid}"];
    }

    private static void Validate(string sid)
    {
        if (!sid.StartsWith("S-1-", StringComparison.Ordinal) || sid.Any(c => !(char.IsDigit(c) || c is '-' or 'S')))
            throw new ArgumentException("Invalid SID.", nameof(sid));
    }
}
