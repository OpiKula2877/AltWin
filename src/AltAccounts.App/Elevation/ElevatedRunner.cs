using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AltAccounts.Core.Config;

namespace AltAccounts.App.Elevation;

/// <summary>
/// Starts this same exe with the "runas" verb, so Windows shows UAC for every admin action.
/// Cancelling UAC means the helper never runs and nothing changes.
/// </summary>
public static class ElevatedRunner
{
    private const int ERROR_CANCELLED = 1223;

    public static async Task<HelperResult> RunAsync(string op, object? payload = null)
    {
        Directory.CreateDirectory(AppPaths.RequestsDir);
        string id = Guid.NewGuid().ToString("N");
        string req = Path.Combine(AppPaths.RequestsDir, id + ".req.json");
        string res = Path.Combine(AppPaths.RequestsDir, id + ".res.json");
        var request = new HelperRequest
        {
            Op = op,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, ConfigStore.Json),
            RequestedBy = Environment.UserName,
        };
        await File.WriteAllTextAsync(req, JsonSerializer.Serialize(request, ConfigStore.Json));

        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--elevated \"{req}\" \"{res}\"",
            };
            using var p = Process.Start(psi);
            if (p is null) return HelperResult.Fail("Could not start the admin helper.");
            await p.WaitForExitAsync();

            if (File.Exists(res))
                return JsonSerializer.Deserialize<HelperResult>(await File.ReadAllTextAsync(res), ConfigStore.Json)
                       ?? HelperResult.Fail("The admin helper returned no result.");
            return HelperResult.Fail($"The admin helper stopped without a result (exit code {p.ExitCode}).");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return HelperResult.Cancel("UAC was cancelled. Nothing changed.");
        }
        finally
        {
            TryDelete(req);
            TryDelete(res);
        }
    }

    public static T? DataAs<T>(HelperResult r) =>
        r.Data is { } d ? d.Deserialize<T>(ConfigStore.Json) : default;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }
}
