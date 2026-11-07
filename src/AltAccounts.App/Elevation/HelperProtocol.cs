using System.Text.Json;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Config;
using AltAccounts.Core.Sharing;

namespace AltAccounts.App.Elevation;

/// <summary>Operations the elevated helper accepts. Each one is checked again inside the helper.</summary>
public static class Ops
{
    public const string AddAccount = "AddAccount";
    public const string EditAccount = "EditAccount";
    public const string RemoveAccount = "RemoveAccount";
    public const string SignOut = "SignOut";
    public const string GrantShare = "GrantShare";
    public const string RemoveShare = "RemoveShare";
    public const string SetShareWarn = "SetShareWarn";
    public const string ApplyPolicy = "ApplyPolicy";
    public const string ProbeQuota = "ProbeQuota";
    public const string MeasureStorage = "MeasureStorage";
    public const string CleanupStale = "CleanupStale";
    public const string Import = "Import";
    public const string InitProgramData = "InitProgramData";
}

/// <summary>Written by the normal app to a file in its own %LocalAppData%; the path is passed to the helper.
/// Never contains a password: the Add-account dialog runs inside the helper.</summary>
public sealed class HelperRequest
{
    public string Op { get; set; } = "";
    public JsonElement? Payload { get; set; }
    public string RequestedBy { get; set; } = "";
}

public sealed class HelperResult
{
    public bool Ok { get; set; }
    public bool Cancelled { get; set; }
    public string Message { get; set; } = "";
    public JsonElement? Data { get; set; }

    public static HelperResult Success(string message, object? data = null) => new()
    {
        Ok = true,
        Message = message,
        Data = data is null ? null : JsonSerializer.SerializeToElement(data, ConfigStore.Json),
    };

    public static HelperResult Fail(string message) => new() { Ok = false, Message = message };
    public static HelperResult Cancel(string message = "Cancelled. Nothing changed.") => new() { Cancelled = true, Message = message };
}

public sealed record EditAccountPayload(string Sid, string? DisplayName, AccountType? Type);
public sealed record SidPayload(string Sid);
public sealed record GrantSharePayload(string Path, string Sid, ShareLevel Level, bool CreateFolder);
public sealed record RemoveSharePayload(string Path, string? Sid);
public sealed record ShareWarnPayload(string Path, double? WarnAtGb);
public sealed record ApplyPolicyPayload(string Sid, AccountPolicy Policy);
public sealed record ImportPayload(ExportBundle Bundle);

public sealed record StorageRow(string Sid, string UserName, string DisplayName, long ProfileBytes, long SharedBytes, double? QuotaGb);
