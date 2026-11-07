using System.Diagnostics;
using System.Text.Json.Serialization;

namespace AltAccounts.Core.Quotas;

[JsonConverter(typeof(JsonStringEnumConverter<QuotaMode>))]
public enum QuotaMode
{
    /// <summary>Not checked yet.</summary>
    Unknown,
    /// <summary>NTFS disk quotas via fsutil: Windows refuses writes past the limit.</summary>
    Enforced,
    /// <summary>The app measures folder sizes and warns. Nothing is blocked.</summary>
    Soft,
}

public enum SoftQuotaState { NoLimit, Ok, Warning, Exceeded }

public static class SoftQuota
{
    public const double WarnFraction = 0.8;

    public static SoftQuotaState Evaluate(long usedBytes, double? limitGb)
    {
        if (limitGb is null or <= 0) return SoftQuotaState.NoLimit;
        double limit = limitGb.Value * 1024d * 1024 * 1024;
        if (usedBytes >= limit) return SoftQuotaState.Exceeded;
        if (usedBytes >= limit * WarnFraction) return SoftQuotaState.Warning;
        return SoftQuotaState.Ok;
    }

    public static string Describe(QuotaMode mode) => mode switch
    {
        QuotaMode.Enforced => "Enforced: NTFS disk quotas (fsutil). Windows blocks new data past the limit. " +
                              "The quota counts every file the account owns on the drive, including files it saves in shared folders.",
        QuotaMode.Soft => "Soft limit: the app measures the profile and shared folders and warns at 80% and 100%. Nothing is blocked.",
        _ => "Not checked yet. Use \"Detect and enable quota support\" in Storage and quotas (needs admin).",
    };
}

public sealed record FsutilResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>NTFS quotas through fsutil. Runs in the elevated helper only.</summary>
public static class QuotaAdmin
{
    /// <summary>"No limit" for an NTFS quota entry (-1 as an unsigned 64-bit value).</summary>
    public const string NoLimit = "18446744073709551615";

    public static FsutilResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo("fsutil.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return new FsutilResult(p.ExitCode, output.Trim());
        }
        catch (Exception ex) { return new FsutilResult(-1, ex.Message); }
    }

    /// <summary>Read-only check: does "fsutil quota query" work on this drive?</summary>
    public static FsutilResult Probe(string drive) => Run("quota", "query", DriveArg(drive));

    /// <summary>Turns on tracking + enforcement and sets the limit (warning at 80%).</summary>
    public static FsutilResult SetLimit(string drive, string account, double limitGb)
    {
        string d = DriveArg(drive);
        long limit = (long)(limitGb * 1024 * 1024 * 1024);
        long warn = (long)(limit * SoftQuota.WarnFraction);
        var r = Run("quota", "enforce", d);
        if (!r.Ok) return r;
        return Run("quota", "modify", d, warn.ToString(), limit.ToString(), account);
    }

    public static FsutilResult ClearLimit(string drive, string account) =>
        Run("quota", "modify", DriveArg(drive), NoLimit, NoLimit, account);

    public static string DriveArg(string drive) => drive.TrimEnd('\\').Length == 2 ? drive.TrimEnd('\\') : drive[..2];
}
