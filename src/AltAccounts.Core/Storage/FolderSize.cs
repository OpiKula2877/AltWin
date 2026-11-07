namespace AltAccounts.Core.Storage;

public static class FolderSize
{
    /// <summary>Total size of all readable files below <paramref name="path"/>. Skips links and unreadable folders.</summary>
    public static long Measure(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        long total = 0;
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                try { total += f.Length; } catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        return total;
    }

    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v:0.0} {units[i]}");
    }

    public static double ToGb(long bytes) => bytes / 1024d / 1024 / 1024;
}
