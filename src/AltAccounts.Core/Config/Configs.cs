using System.Text.Json.Serialization;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Sharing;

namespace AltAccounts.Core.Config;

/// <summary>Per-account options set by an admin. Stored in the shared config, keyed by SID.</summary>
public sealed class AccountPolicy
{
    /// <summary>Kept so export/import can map accounts on another PC (SIDs differ).</summary>
    public string UserName { get; set; } = "";
    public bool HideFromPopup { get; set; }
    public string? AccentColor { get; set; }
    /// <summary>The app exits right after start in this account (no Alt popup there).</summary>
    public bool DisableAppHere { get; set; }
    public double? QuotaGb { get; set; }
    public int? IdleLockMinutes { get; set; }
    public int? DailyLimitMinutes { get; set; }
    /// <summary>"M-F,08:00-20:00;Sa-Su,10:00-18:00" or null for any time.</summary>
    public string? SignInHours { get; set; }

    public AccountPolicy Clone() => (AccountPolicy)MemberwiseClone();
}

/// <summary>%ProgramData%\AltAccounts\config.json — writable by admins only.</summary>
public sealed class SharedConfig
{
    public int Version { get; set; } = 1;
    public List<ShareEntry> Shares { get; set; } = [];
    public Dictionary<string, AccountPolicy> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public QuotaMode QuotaMode { get; set; } = QuotaMode.Unknown;
    public DateTime? QuotaCheckedUtc { get; set; }
    public string? QuotaDetail { get; set; }

    public AccountPolicy PolicyFor(string sid) =>
        Policies.TryGetValue(sid, out var p) ? p : new AccountPolicy();

    public ShareEntry? FindShare(string path) =>
        Shares.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode { System, Light, Dark }

[JsonConverter(typeof(JsonStringEnumConverter<TriggerMode>))]
public enum TriggerMode { AltTap, Hotkey }

/// <summary>%LocalAppData%\AltAccounts\user.json — the signed-in user's own preferences.</summary>
public sealed class UserConfig
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public TriggerMode Trigger { get; set; } = TriggerMode.AltTap;
    /// <summary>E.g. "Ctrl+Alt+Space". Used when <see cref="Trigger"/> is Hotkey.</summary>
    public string? Hotkey { get; set; }
    public bool IncludeRightAlt { get; set; }
    public bool SeenSwitchHint { get; set; }
    public bool NotifySessionChanges { get; set; } = true;
    public string? UsageDate { get; set; }
    public int UsageMinutes { get; set; }
}

public sealed class ExportBundle
{
    public int Version { get; set; } = 1;
    public string MachineName { get; set; } = Environment.MachineName;
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    public SharedConfig Shared { get; set; } = new();
}
