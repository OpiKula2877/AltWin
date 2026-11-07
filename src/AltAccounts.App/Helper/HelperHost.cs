using System.Text.Json;
using System.Windows;
using AltAccounts.App.Elevation;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;

namespace AltAccounts.App.Helper;

/// <summary>Entry point of the elevated helper (--elevated &lt;request&gt; &lt;result&gt;).</summary>
public static class HelperHost
{
    public static async Task<int> RunAsync(string requestPath, string resultPath)
    {
        HelperResult result;
        try
        {
            if (!LocalAccountService.IsCurrentProcessElevated())
                result = HelperResult.Fail("The admin helper is not running with admin rights.");
            else
            {
                var request = JsonSerializer.Deserialize<HelperRequest>(await File.ReadAllTextAsync(requestPath), ConfigStore.Json)
                              ?? throw new InvalidOperationException("Empty request.");
                if (!ProgramDataSetup.Exists) ProgramDataSetup.EnsureSecured();
                result = await DispatchAsync(request);
            }
        }
        catch (Exception ex)
        {
            AuditLog.Append("HelperError", ex.Message);
            result = HelperResult.Fail(ex.Message);
        }

        try { await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, ConfigStore.Json)); }
        catch (Exception) { return 3; }
        return result.Ok ? 0 : result.Cancelled ? 2 : 1;
    }

    private static T Payload<T>(HelperRequest r) =>
        r.Payload is { } p ? p.Deserialize<T>(ConfigStore.Json) ?? throw new InvalidOperationException("Bad request.")
                           : throw new InvalidOperationException("Missing request data.");

    private static async Task<HelperResult> DispatchAsync(HelperRequest r)
    {
        string by = r.RequestedBy;
        switch (r.Op)
        {
            case Ops.AddAccount:
            {
                var w = new AddAccountWindow();
                if (w.ShowDialog() != true || w.CreatedSid is null) return HelperResult.Cancel();
                var cfg = ConfigStore.LoadShared();
                cfg.Policies[w.CreatedSid] = new AccountPolicy { UserName = w.CreatedUserName! };
                ConfigStore.Save(AppPaths.SharedConfigPath, cfg);
                AuditLog.Append("AddAccount", $"{w.CreatedUserName} ({w.CreatedType})" +
                    (string.Equals(by, Environment.UserName, StringComparison.OrdinalIgnoreCase) ? "" : $" (requested by {by})"));
                return HelperResult.Success($"Created {w.CreatedUserName}. They sign in on the Windows sign-in screen with the password you typed and can change it later.",
                    new SidPayload(w.CreatedSid));
            }

            case Ops.RemoveAccount:
            {
                var sid = Payload<SidPayload>(r).Sid;
                var acct = LocalAccountService.GetAll().FirstOrDefault(a => a.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase));
                if (acct is null) return HelperResult.Fail("That account no longer exists.");
                AccountAdmin.EnsureCanDelete(sid);
                var w = new RemoveAccountWindow(acct);
                if (w.ShowDialog() != true) return HelperResult.Cancel();
                return await Busy($"Removing {acct.UserName}…", () => HelperOperations.RemoveAccount(acct, w.DeleteProfile, by));
            }

            case Ops.EditAccount: return await Busy("Updating account…", () => HelperOperations.EditAccount(Payload<EditAccountPayload>(r), by));
            case Ops.SignOut: return await Busy("Signing out…", () => HelperOperations.SignOut(Payload<SidPayload>(r), by));
            case Ops.GrantShare: return await Busy("Setting folder permissions… (large folders take a while)", () => HelperOperations.GrantShare(Payload<GrantSharePayload>(r), by));
            case Ops.RemoveShare: return await Busy("Removing folder permissions…", () => HelperOperations.RemoveShare(Payload<RemoveSharePayload>(r), by));
            case Ops.SetShareWarn: return HelperOperations.SetShareWarn(Payload<ShareWarnPayload>(r), by);
            case Ops.ApplyPolicy: return await Busy("Saving options…", () => HelperOperations.ApplyPolicy(Payload<ApplyPolicyPayload>(r), by));
            case Ops.ProbeQuota: return await Busy("Checking disk quota support…", () => HelperOperations.ProbeQuota(by));
            case Ops.MeasureStorage: return await Busy("Measuring folders…", HelperOperations.MeasureStorage);
            case Ops.CleanupStale: return await Busy("Cleaning up…", () => HelperOperations.CleanupStale(by));
            case Ops.Import: return await Busy("Importing…", () => HelperOperations.Import(Payload<ImportPayload>(r), by));
            case Ops.InitProgramData:
                ProgramDataSetup.EnsureSecured();
                return HelperResult.Success("Shared settings folder is ready.");
            default:
                return HelperResult.Fail($"Unknown operation '{r.Op}'.");
        }
    }

    /// <summary>Runs the work off the UI thread with a small progress window.</summary>
    private static async Task<HelperResult> Busy(string text, Func<HelperResult> work)
    {
        var w = new ProgressWindow(text);
        var timer = Task.Delay(400);
        var task = Task.Run(work);
        if (await Task.WhenAny(task, timer) == timer) w.Show();
        try { return await task; }
        finally { w.Close(); }
    }
}
