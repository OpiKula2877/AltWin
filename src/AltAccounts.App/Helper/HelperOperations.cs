using System.Diagnostics;
using System.Text.RegularExpressions;
using AltAccounts.App.Elevation;
using AltAccounts.App.Services;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;
using AltAccounts.Core.Policy;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Sessions;
using AltAccounts.Core.Sharing;
using AltAccounts.Core.Storage;

namespace AltAccounts.App.Helper;

/// <summary>The work done inside the elevated helper. Every input is validated again here.</summary>
public static partial class HelperOperations
{
    private static AccountInfo RequireAccount(string sid) =>
        LocalAccountService.GetAll().FirstOrDefault(a => a.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("That account no longer exists.");

    private static string By(string requestedBy) =>
        string.Equals(requestedBy, Environment.UserName, StringComparison.OrdinalIgnoreCase) ? "" : $" (requested by {requestedBy})";

    // ---------- accounts ----------

    public static HelperResult EditAccount(EditAccountPayload p, string requestedBy)
    {
        var acct = RequireAccount(p.Sid);
        var changes = new List<string>();
        if (p.DisplayName is not null && p.DisplayName != acct.DisplayName)
        {
            AccountAdmin.SetDisplayName(p.Sid, p.DisplayName);
            changes.Add($"display name \"{p.DisplayName}\"");
        }
        if (p.Type is { } t && t != acct.Type)
        {
            AccountAdmin.SetType(p.Sid, t);
            changes.Add(t == AccountType.Admin ? "type Admin" : "type Standard");
        }
        if (changes.Count == 0) return HelperResult.Success("No changes.");
        AuditLog.Append("EditAccount", $"{acct.UserName}: {string.Join(", ", changes)}{By(requestedBy)}");
        return HelperResult.Success($"Updated {acct.UserName}: {string.Join(", ", changes)}.");
    }

    /// <summary>Signs out every session of the account except the helper's own session.</summary>
    public static int SignOutSessions(string userName)
    {
        int count = 0;
        foreach (var s in SessionService.GetSessions())
        {
            if (!s.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)) continue;
            if (s.SessionId == SessionService.CurrentSessionId) continue;
            SessionService.LogOff(s.SessionId);
            count++;
        }
        return count;
    }

    public static HelperResult SignOut(SidPayload p, string requestedBy)
    {
        var acct = RequireAccount(p.Sid);
        int n = SignOutSessions(acct.UserName);
        AuditLog.Append("SignOut", $"{acct.UserName}: {n} session(s){By(requestedBy)}");
        return HelperResult.Success(n == 0 ? $"{acct.UserName} was not signed in." : $"Signed out {acct.UserName}.");
    }

    public static HelperResult RemoveAccount(AccountInfo acct, bool deleteProfile, string requestedBy)
    {
        AccountAdmin.EnsureCanDelete(acct.Sid); // check before changing anything
        var notes = new List<string>();
        if (SessionService.GetSessions().Any(s => s.UserName.Equals(acct.UserName, StringComparison.OrdinalIgnoreCase)))
        {
            SignOutSessions(acct.UserName);
            for (int i = 0; i < 30 && SessionService.GetSessions().Any(s => s.UserName.Equals(acct.UserName, StringComparison.OrdinalIgnoreCase)); i++)
                Thread.Sleep(1000);
            notes.Add("signed out first");
        }

        // Remove this account's grants first, while the SID still resolves.
        var cfg = ConfigStore.LoadShared();
        foreach (var share in cfg.Shares)
        {
            foreach (var g in share.Grants.Where(g => g.Sid.Equals(acct.Sid, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (!g.PreExisting && Directory.Exists(share.Path))
                {
                    try { ShareAdmin.Remove(share.Path, g.Sid); } catch (Exception ex) { notes.Add($"could not remove grant on {share.Path}: {ex.Message}"); }
                }
                share.Grants.Remove(g);
            }
        }
        cfg.Shares.RemoveAll(s => s.Grants.Count == 0);
        cfg.Policies.Remove(acct.Sid);
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);

        AccountAdmin.DeleteUser(acct.Sid, deleteProfile: false);
        if (deleteProfile)
        {
            Exception? last = null;
            for (int i = 0; i < 10; i++)
            {
                try { AccountAdmin.DeleteProfile(acct.Sid); last = null; break; }
                catch (Exception ex) { last = ex; Thread.Sleep(3000); }
            }
            notes.Add(last is null ? "profile folder deleted" : $"profile folder NOT deleted: {last.Message}");
        }
        else notes.Add($"profile folder kept ({acct.ProfilePath ?? "none"})");

        AuditLog.Append("RemoveAccount", $"{acct.UserName}: {string.Join("; ", notes)}{By(requestedBy)}");
        return HelperResult.Success($"Removed {acct.UserName}: {string.Join("; ", notes)}.");
    }

    // ---------- shared folders ----------

    public static HelperResult GrantShare(GrantSharePayload p, string requestedBy)
    {
        var check = SharePathValidator.ForThisPc().Check(p.Path);
        if (!check.Ok) return HelperResult.Fail(check.Error!);
        string path = SharePathValidator.Normalize(p.Path);
        var acct = RequireAccount(p.Sid);

        if (!Directory.Exists(path))
        {
            if (!p.CreateFolder) return HelperResult.Fail($"The folder {path} does not exist.");
            Directory.CreateDirectory(path);
        }

        var cfg = ConfigStore.LoadShared();
        var share = cfg.FindShare(path);
        if (share is null) { share = new ShareEntry { Path = path }; cfg.Shares.Add(share); }
        var existing = share.Grants.FirstOrDefault(g => g.Sid.Equals(acct.Sid, StringComparison.OrdinalIgnoreCase));

        bool preExisting;
        if (existing is not null)
        {
            preExisting = existing.PreExisting;
            // Replace the app's own grant (e.g. Modify → Read). A pre-existing permission is never removed.
            if (!existing.PreExisting) ShareAdmin.Remove(path, acct.Sid);
            share.Grants.Remove(existing);
        }
        else preExisting = ShareAdmin.HasExplicitGrant(path, acct.Sid);

        ShareAdmin.Grant(path, acct.Sid, p.Level);
        share.Grants.Add(new ShareGrant { Sid = acct.Sid, UserName = acct.UserName, Level = p.Level, PreExisting = preExisting });
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("GrantShare", $"{path}: {acct.UserName} {p.Level}{By(requestedBy)}");

        string msg = $"{acct.UserName} now has {p.Level} access to {path}.";
        if (preExisting) msg += " This account already had its own permission on the folder, so removing the share later will leave that permission in place.";
        if (check.Warning is not null) msg += " " + check.Warning;
        return HelperResult.Success(msg);
    }

    public static HelperResult RemoveShare(RemoveSharePayload p, string requestedBy)
    {
        var cfg = ConfigStore.LoadShared();
        var share = cfg.FindShare(p.Path);
        if (share is null) return HelperResult.Fail("That folder is not in the share list.");
        var targets = share.Grants.Where(g => p.Sid is null || g.Sid.Equals(p.Sid, StringComparison.OrdinalIgnoreCase)).ToList();
        var notes = new List<string>();
        foreach (var g in targets)
        {
            if (g.PreExisting)
                notes.Add($"{g.UserName}: left unchanged (the account had its own permission before the app)");
            else if (Directory.Exists(share.Path))
            {
                ShareAdmin.Remove(share.Path, g.Sid);
                notes.Add($"{g.UserName}: access removed");
            }
            share.Grants.Remove(g);
        }
        if (share.Grants.Count == 0) cfg.Shares.Remove(share);
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("RemoveShare", $"{share.Path}: {string.Join("; ", notes)}{By(requestedBy)}");
        return HelperResult.Success(notes.Count == 0 ? "Nothing to remove." : string.Join("\n", notes));
    }

    public static HelperResult SetShareWarn(ShareWarnPayload p, string requestedBy)
    {
        if (p.WarnAtGb is <= 0 or > 1_000_000) return HelperResult.Fail("Enter a size in GB greater than 0.");
        var cfg = ConfigStore.LoadShared();
        var share = cfg.FindShare(p.Path);
        if (share is null) return HelperResult.Fail("That folder is not in the share list.");
        share.WarnAtGb = p.WarnAtGb;
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("SetShareWarn", $"{share.Path}: {(p.WarnAtGb is null ? "off" : p.WarnAtGb + " GB")}{By(requestedBy)}");
        return HelperResult.Success("Saved.");
    }

    public static HelperResult CleanupStale(string requestedBy)
    {
        var cfg = ConfigStore.LoadShared();
        var items = StaleScan.Find(cfg);
        var notes = new List<string>();
        foreach (var item in items)
        {
            if (item.Path.Length == 0 && item.Sid is not null)
            {
                cfg.Policies.Remove(item.Sid);
                notes.Add(item.Description + ": removed");
                continue;
            }
            var share = cfg.FindShare(item.Path);
            if (share is null) continue;
            if (item.Sid is null)
            {
                cfg.Shares.Remove(share); // folder is gone, nothing left to un-grant
                notes.Add(item.Description + ": removed from list");
                continue;
            }
            var g = share.Grants.FirstOrDefault(x => x.Sid.Equals(item.Sid, StringComparison.OrdinalIgnoreCase));
            if (g is null) continue;
            if (!g.PreExisting)
            {
                try { ShareAdmin.Remove(share.Path, g.Sid); } catch (Exception ex) { notes.Add($"{item.Description}: icacls failed: {ex.Message}"); continue; }
            }
            share.Grants.Remove(g);
            if (share.Grants.Count == 0) cfg.Shares.Remove(share);
            notes.Add(item.Description + ": removed");
        }
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("CleanupStale", $"{notes.Count} item(s){By(requestedBy)}");
        return HelperResult.Success(notes.Count == 0 ? "Nothing stale found." : string.Join("\n", notes));
    }

    // ---------- policies, quotas, storage ----------

    [GeneratedRegex("^#([0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    public static string? ValidatePolicy(AccountPolicy p)
    {
        if (p.IdleLockMinutes is < 1 or > 1440) return "Idle lock: 1 to 1440 minutes, or empty.";
        if (p.DailyLimitMinutes is < 1 or > 1440) return "Daily limit: 1 to 1440 minutes, or empty.";
        if (p.QuotaGb is <= 0 or > 1_000_000) return "Disk limit: more than 0 GB, or empty.";
        if (!string.IsNullOrWhiteSpace(p.SignInHours) && !SignInHours.TryParse(p.SignInHours, out _, out var err)) return "Sign-in hours: " + err;
        if (!string.IsNullOrWhiteSpace(p.AccentColor) && !HexColor().IsMatch(p.AccentColor)) return "Accent colour: use #RRGGBB.";
        return null;
    }

    public static HelperResult ApplyPolicy(ApplyPolicyPayload p, string requestedBy)
    {
        var acct = RequireAccount(p.Sid);
        var policy = p.Policy.Clone();
        if (ValidatePolicy(policy) is { } error) return HelperResult.Fail(error);
        policy.UserName = acct.UserName;
        policy.SignInHours = string.IsNullOrWhiteSpace(policy.SignInHours) ? null : NormalizeHours(policy.SignInHours);
        policy.AccentColor = string.IsNullOrWhiteSpace(policy.AccentColor) ? null : policy.AccentColor;

        var cfg = ConfigStore.LoadShared();
        var old = cfg.PolicyFor(acct.Sid);
        var notes = new List<string>();

        if (!string.Equals(old.SignInHours, policy.SignInHours, StringComparison.Ordinal))
        {
            var (code, output) = RunNetUser(acct.UserName, policy.SignInHours ?? "all");
            if (code != 0) return HelperResult.Fail($"net user /times failed: {output}");
            notes.Add(policy.SignInHours is null ? "sign-in hours: any time" : $"sign-in hours: {policy.SignInHours} (enforced by Windows for new sign-ins)");
        }

        if (old.QuotaGb != policy.QuotaGb)
        {
            if (cfg.QuotaMode == QuotaMode.Unknown) DetectQuotaMode(cfg);
            if (cfg.QuotaMode == QuotaMode.Enforced)
            {
                string drive = Path.GetPathRoot(acct.ProfilePath ?? "C:\\") ?? "C:\\";
                string who = $"{Environment.MachineName}\\{acct.UserName}";
                var r = policy.QuotaGb is { } gb ? QuotaAdmin.SetLimit(drive, who, gb) : QuotaAdmin.ClearLimit(drive, who);
                if (r.Ok) notes.Add(policy.QuotaGb is null ? "disk limit removed" : $"disk limit {policy.QuotaGb} GB (NTFS quota, enforced)");
                else
                {
                    cfg.QuotaMode = QuotaMode.Soft;
                    cfg.QuotaDetail = "fsutil failed: " + r.Output;
                    notes.Add($"NTFS quota failed ({r.Output}); switched to soft limit");
                }
            }
            else notes.Add(policy.QuotaGb is null ? "disk limit removed" : $"disk limit {policy.QuotaGb} GB (soft: the app warns only)");
        }

        cfg.Policies[acct.Sid] = policy;
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("ApplyPolicy", $"{acct.UserName}: idle={policy.IdleLockMinutes} daily={policy.DailyLimitMinutes} hours={policy.SignInHours} quota={policy.QuotaGb} hidden={policy.HideFromPopup} disabled={policy.DisableAppHere}{By(requestedBy)}");
        return HelperResult.Success("Saved options for " + acct.UserName + (notes.Count > 0 ? ":\n" + string.Join("\n", notes) : "."));
    }

    private static string NormalizeHours(string text) =>
        SignInHours.TryParse(text, out var h, out _) ? h!.ToNetUserTimes() : text;

    private static (int, string) RunNetUser(string userName, string times)
    {
        var psi = new ProcessStartInfo("net.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "user", userName, "/times:" + times }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, output.Trim());
    }

    /// <summary>Checks whether NTFS quotas work on C:. If tracking is off, turns tracking on and checks again.</summary>
    public static void DetectQuotaMode(SharedConfig cfg)
    {
        string drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var r = QuotaAdmin.Probe(drive);
        if (!r.Ok)
        {
            QuotaAdmin.Run("quota", "track", QuotaAdmin.DriveArg(drive));
            r = QuotaAdmin.Probe(drive);
        }
        cfg.QuotaMode = r.Ok ? QuotaMode.Enforced : QuotaMode.Soft;
        cfg.QuotaCheckedUtc = DateTime.UtcNow;
        cfg.QuotaDetail = string.Join(" ", r.Output.Split('\n').Take(3).Select(l => l.Trim()));
    }

    public static HelperResult ProbeQuota(string requestedBy)
    {
        var cfg = ConfigStore.LoadShared();
        DetectQuotaMode(cfg);
        var notes = new List<string>();
        if (cfg.QuotaMode == QuotaMode.Enforced)
        {
            foreach (var (sid, pol) in cfg.Policies.Where(kv => kv.Value.QuotaGb is not null))
            {
                var acct = LocalAccountService.GetAll().FirstOrDefault(a => a.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase));
                if (acct is null) continue;
                string drive = Path.GetPathRoot(acct.ProfilePath ?? "C:\\") ?? "C:\\";
                var r = QuotaAdmin.SetLimit(drive, $"{Environment.MachineName}\\{acct.UserName}", pol.QuotaGb!.Value);
                notes.Add($"{acct.UserName}: {(r.Ok ? $"{pol.QuotaGb} GB enforced" : "failed: " + r.Output)}");
            }
        }
        ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
        AuditLog.Append("ProbeQuota", $"{cfg.QuotaMode}{By(requestedBy)}");
        return HelperResult.Success($"Quota mode: {cfg.QuotaMode}.\n{SoftQuota.Describe(cfg.QuotaMode)}" +
                                    (notes.Count > 0 ? "\n" + string.Join("\n", notes) : ""));
    }

    public static HelperResult MeasureStorage()
    {
        var cfg = ConfigStore.LoadShared();
        var shareSizes = cfg.Shares.ToDictionary(s => s.Path, s => FolderSize.Measure(s.Path), StringComparer.OrdinalIgnoreCase);
        var rows = new List<StorageRow>();
        foreach (var a in LocalAccountService.GetListable())
        {
            long profile = a.ProfilePath is null ? 0 : FolderSize.Measure(a.ProfilePath);
            long shared = cfg.Shares
                .Where(s => a.Type == AccountType.Admin || s.Grants.Any(g => g.Sid.Equals(a.Sid, StringComparison.OrdinalIgnoreCase)))
                .Sum(s => shareSizes[s.Path]);
            rows.Add(new StorageRow(a.Sid, a.UserName, a.ShownName, profile, shared, cfg.PolicyFor(a.Sid).QuotaGb));
        }
        return HelperResult.Success("Measured.", rows);
    }

    // ---------- export / import ----------

    public static HelperResult Import(ImportPayload p, string requestedBy)
    {
        var notes = new List<string>();
        var bundle = p.Bundle;
        var local = LocalAccountService.GetAll();
        string? MapSid(string userName) =>
            local.FirstOrDefault(a => a.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))?.Sid;

        foreach (var pol in (bundle.Shared.Policies ?? []).Values)
        {
            var sid = MapSid(pol.UserName);
            if (sid is null) { notes.Add($"Options for '{pol.UserName}': skipped (no such account here)"); continue; }
            var r = ApplyPolicy(new ApplyPolicyPayload(sid, pol), requestedBy);
            notes.Add($"Options for '{pol.UserName}': {(r.Ok ? "imported" : r.Message)}");
        }

        foreach (var share in bundle.Shared.Shares ?? [])
        {
            if (!Directory.Exists(share.Path)) { notes.Add($"{share.Path}: skipped (folder does not exist here)"); continue; }
            foreach (var g in share.Grants)
            {
                var sid = MapSid(g.UserName);
                if (sid is null) { notes.Add($"{share.Path}: '{g.UserName}' skipped (no such account here)"); continue; }
                var r = GrantShare(new GrantSharePayload(share.Path, sid, g.Level, false), requestedBy);
                notes.Add($"{share.Path}: '{g.UserName}' {(r.Ok ? g.Level.ToString() : r.Message)}");
            }
            if (share.WarnAtGb is not null && ConfigStore.LoadShared().FindShare(share.Path) is not null)
                SetShareWarn(new ShareWarnPayload(SharePathValidator.Normalize(share.Path), share.WarnAtGb), requestedBy);
        }
        AuditLog.Append("Import", $"from {bundle.MachineName}, {notes.Count} item(s){By(requestedBy)}");
        return HelperResult.Success(notes.Count == 0 ? "Nothing to import." : string.Join("\n", notes));
    }
}
