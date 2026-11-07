using System.Globalization;
using AltAccounts.Core.Config;

namespace AltAccounts.Core.Audit;

public sealed record AuditEntry(DateTime TimeUtc, string User, string Action, string Details)
{
    public string LocalTime => TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>
/// Append-only text log: one entry per line, tab separated. Never pass secrets to it.
/// Writes to %ProgramData%\AltAccounts\audit.log, or the per-user log when that is not writable.
/// </summary>
public static class AuditLog
{
    public static void Append(string action, string details = "")
    {
        string line = string.Join('\t',
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Clean(Environment.UserName), Clean(action), Clean(details)) + Environment.NewLine;
        if (TryAppend(AppPaths.AuditPath, line)) return;
        try
        {
            Directory.CreateDirectory(AppPaths.LocalDir);
            TryAppend(AppPaths.LocalAuditPath, line);
        }
        catch (Exception) { /* logging must never break the app */ }
    }

    private const uint FILE_APPEND_DATA = 0x0004;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_READ_WRITE = 0x3;
    private const uint OPEN_EXISTING = 3;
    private const uint OPEN_ALWAYS = 4;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool WriteFile(Microsoft.Win32.SafeHandles.SafeFileHandle h, byte[] buffer, int count,
        out int written, IntPtr overlapped);

    /// <summary>Opens with FILE_APPEND_DATA only, so it works with the append-only permission
    /// on the shared log, and Windows always writes at the end of the file.</summary>
    private static bool TryAppend(string path, string line)
    {
        try
        {
            // Only the installer or helper creates the shared log (with its ACL); the per-user log may be created here.
            uint disposition = path == AppPaths.AuditPath ? OPEN_EXISTING : OPEN_ALWAYS;
            using var h = CreateFileW(path, FILE_APPEND_DATA | SYNCHRONIZE, FILE_SHARE_READ_WRITE, IntPtr.Zero, disposition, 0, IntPtr.Zero);
            if (h.IsInvalid) return false;
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            return WriteFile(h, bytes, bytes.Length, out _, IntPtr.Zero);
        }
        catch (Exception) { return false; }
    }

    public static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static List<AuditEntry> ReadAll(int max = 2000)
    {
        var list = new List<AuditEntry>();
        foreach (var path in new[] { AppPaths.AuditPath, AppPaths.LocalAuditPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                while (sr.ReadLine() is { } line)
                    if (Parse(line) is { } e) list.Add(e);
            }
            catch (Exception) { }
        }
        return list.OrderByDescending(e => e.TimeUtc).Take(max).ToList();
    }

    public static AuditEntry? Parse(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length < 3) return null;
        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) return null;
        return new AuditEntry(t.ToUniversalTime(), parts[1], parts[2], parts.Length > 3 ? parts[3] : "");
    }
}
