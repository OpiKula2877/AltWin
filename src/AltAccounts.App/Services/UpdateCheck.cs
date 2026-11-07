using System.Diagnostics;

namespace AltAccounts.App.Services;

/// <summary>Idea 20: asks winget whether a newer version is available. Read-only; it installs nothing.</summary>
public static class UpdateCheck
{
    public const string PackageId = "OpiKula2877.AltWin";

    public static async Task<string> CheckAsync()
    {
        var psi = new ProcessStartInfo("winget.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "list", "--id", PackageId, "--exact", "--upgrade-available", "--disable-interactivity" })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var lines = output.Split('\n').Select(l => l.TrimEnd('\r'))
                .Where(l => l.Trim().Length > 1 && !l.TrimStart().StartsWith('-') && !l.Contains('\b')).ToList();
            bool hasRow = lines.Any(l => l.Contains(PackageId, StringComparison.OrdinalIgnoreCase));
            return hasRow
                ? "An update is available. Run this in a terminal:\n\nwinget upgrade --id " + PackageId
                : "No update found through winget. (If the app was not installed through winget, winget cannot see it.)";
        }
        catch (Exception ex)
        {
            return "winget is not available: " + ex.Message;
        }
    }
}
