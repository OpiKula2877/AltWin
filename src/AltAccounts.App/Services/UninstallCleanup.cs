using System.Diagnostics;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;
using AltAccounts.Core.Sharing;
using Microsoft.Win32;

namespace AltAccounts.App.Services;

/// <summary>
/// Run by the uninstaller (elevated): removes every share grant the app recorded and every
/// "AltAccounts_*" HKCU Run value it created in any profile. The HKLM Run value is removed by the installer.
/// config.json is kept unless deleteConfig is set.
/// </summary>
public static class UninstallCleanup
{
    public static int Run(bool deleteConfig)
    {
        if (!LocalAccountService.IsCurrentProcessElevated()) return 5;
        int problems = 0;

        var cfg = ConfigStore.LoadShared();
        foreach (var share in cfg.Shares)
        {
            if (!Directory.Exists(share.Path)) continue;
            foreach (var g in share.Grants.Where(g => !g.PreExisting))
            {
                try { ShareAdmin.Remove(share.Path, g.Sid); }
                catch (Exception) { problems++; }
            }
        }
        AuditLog.Append("Uninstall", $"Removed app grants on {cfg.Shares.Count} folder(s)");
        cfg.Shares.Clear();

        problems += CleanRunValues();

        try
        {
            if (deleteConfig)
            {
                if (Directory.Exists(AppPaths.ProgramDataDir)) Directory.Delete(AppPaths.ProgramDataDir, recursive: true);
            }
            else if (File.Exists(AppPaths.SharedConfigPath))
            {
                ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
            }
        }
        catch (Exception) { problems++; }
        return problems == 0 ? 0 : 1;
    }

    private static int CleanRunValues()
    {
        int problems = 0;
        using var profiles = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profiles is null) return 0;
        foreach (var sid in profiles.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var loaded = Registry.Users.OpenSubKey(sid + @"\" + AutostartApps.RunKey, writable: true);
                if (loaded is not null) { DeleteOurValues(loaded); continue; }

                // Not signed in: load the hive with reg.exe, clean it, unload it.
                string? profile = LocalAccountService.GetProfilePath(sid);
                string hive = profile is null ? "" : Path.Combine(profile, "NTUSER.DAT");
                if (!File.Exists(hive)) continue;
                const string mount = "AltAccountsCleanup";
                if (Reg("load", $@"HKU\{mount}", hive) != 0) { problems++; continue; }
                try
                {
                    using var key = Registry.Users.OpenSubKey(mount + @"\" + AutostartApps.RunKey, writable: true);
                    if (key is not null) DeleteOurValues(key);
                }
                finally
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Reg("unload", $@"HKU\{mount}");
                }
            }
            catch (Exception) { problems++; }
        }
        return problems;
    }

    private static void DeleteOurValues(RegistryKey key)
    {
        foreach (var name in key.GetValueNames())
            if (name.StartsWith(AutostartApps.Prefix, StringComparison.OrdinalIgnoreCase) || name == "AltAccounts")
                key.DeleteValue(name, false);
    }

    private static int Reg(params string[] args)
    {
        var psi = new ProcessStartInfo("reg.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
