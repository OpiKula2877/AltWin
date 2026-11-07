using Microsoft.Win32;

namespace AltAccounts.App.Services;

/// <summary>
/// Idea 16: apps that start when the current account signs in.
/// Writes only this account's HKCU Run key, and only values named "AltAccounts_*",
/// so the uninstaller can find and remove exactly what the app created.
/// </summary>
public static class AutostartApps
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string Prefix = "AltAccounts_";

    public sealed record Entry(string ValueName, string Command)
    {
        public string Name => ValueName[Prefix.Length..];
    }

    public static List<Entry> List()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key is null) return [];
        return key.GetValueNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => new Entry(n, key.GetValue(n) as string ?? ""))
            .ToList();
    }

    public static void Add(string exePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(exePath);
        string clean = new(baseName.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        string name = Prefix + (clean.Length > 0 ? clean : "App");
        int i = 2;
        while (key.GetValue(name) is not null) name = $"{Prefix}{clean}{i++}";
        key.SetValue(name, $"\"{exePath}\"");
    }

    public static void Remove(string valueName)
    {
        if (!valueName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return; // never touch other entries
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    /// <summary>The installer writes one machine-wide value so the app starts in every account.</summary>
    public static string? MachineAutostartCommand()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RunKey);
        return key?.GetValue("AltAccounts") as string;
    }
}
