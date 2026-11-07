namespace AltAccounts.Core.Sharing;

public sealed record PathCheck(bool Ok, string? Error = null, string? Warning = null)
{
    public static PathCheck Fail(string error) => new(false, error);
}

/// <summary>Refuses folders that must never be shared (spec §4.4).</summary>
public sealed class SharePathValidator
{
    private readonly string[] _protectedTrees;
    private readonly string _usersRoot;
    private readonly Func<string, bool> _isLocalFixedOrRemovable;

    public SharePathValidator(IEnumerable<string> protectedTrees, string usersRoot, Func<string, bool>? isLocalDrive = null)
    {
        _protectedTrees = protectedTrees.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).ToArray();
        _usersRoot = Normalize(usersRoot);
        _isLocalFixedOrRemovable = isLocalDrive ?? DefaultIsLocalDrive;
    }

    public static SharePathValidator ForThisPc()
    {
        var trees = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // C:\ProgramData
        };
        string profiles = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
        return new SharePathValidator(trees, profiles);
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim().Trim('"'));
        return full.Length > 3 ? full.TrimEnd('\\', '/') : full;
    }

    public PathCheck Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathCheck.Fail("Pick a folder.");
        string p;
        try
        {
            if (!Path.IsPathFullyQualified(path.Trim().Trim('"'))) return PathCheck.Fail("Use a full path such as C:\\Shared.");
            p = Normalize(path);
        }
        catch (Exception) { return PathCheck.Fail("That is not a valid folder path."); }

        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return PathCheck.Fail("Network paths cannot be shared here. Use a folder on this PC.");

        string? root = Path.GetPathRoot(p);
        if (root is null || string.Equals(Normalize(root), p, StringComparison.OrdinalIgnoreCase))
            return PathCheck.Fail("A whole drive cannot be shared. Use a dedicated folder such as C:\\Shared.");

        if (!_isLocalFixedOrRemovable(root)) return PathCheck.Fail("Only folders on local drives can be shared.");

        foreach (var tree in _protectedTrees)
            if (IsSameOrUnder(p, tree))
                return PathCheck.Fail($"System folders cannot be shared ({tree}). Use a dedicated folder such as C:\\Shared.");

        if (string.Equals(p, _usersRoot, StringComparison.OrdinalIgnoreCase))
            return PathCheck.Fail("The Users folder cannot be shared.");

        if (IsSameOrUnder(p, _usersRoot))
        {
            string rel = p[_usersRoot.Length..].TrimStart('\\');
            if (!rel.Contains('\\'))
                return PathCheck.Fail("A whole user profile cannot be shared. Use a dedicated folder such as C:\\Shared.");
            return new PathCheck(true, Warning:
                "This folder is inside a user profile. A dedicated folder such as C:\\Shared is easier to manage.");
        }
        return new PathCheck(true);
    }

    public static bool IsSameOrUnder(string path, string tree)
    {
        if (string.Equals(path, tree, StringComparison.OrdinalIgnoreCase)) return true;
        string t = tree.EndsWith('\\') ? tree : tree + "\\";
        return path.StartsWith(t, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DefaultIsLocalDrive(string root)
    {
        try
        {
            var d = new DriveInfo(root);
            return d.DriveType is DriveType.Fixed or DriveType.Removable;
        }
        catch (Exception) { return false; }
    }
}
