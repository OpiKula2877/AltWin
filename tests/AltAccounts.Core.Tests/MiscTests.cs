using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;
using AltAccounts.Core.Edition;
using AltAccounts.Core.Input;
using AltAccounts.Core.Native;
using AltAccounts.Core.Policy;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Sessions;
using AltAccounts.Core.Sharing;
using AltAccounts.Core.Storage;

namespace AltAccounts.Core.Tests;

public class EditionTests
{
    [Theory]
    [InlineData("Core", WindowsEditionKind.Home)]
    [InlineData("CoreSingleLanguage", WindowsEditionKind.Home)]
    [InlineData("Professional", WindowsEditionKind.Pro)]
    [InlineData("ProfessionalWorkstation", WindowsEditionKind.Pro)]
    [InlineData("Enterprise", WindowsEditionKind.Other)]
    [InlineData(null, WindowsEditionKind.Other)]
    public void Classify(string? id, WindowsEditionKind expected) => Assert.Equal(expected, EditionInfo.Classify(id));

    [Fact]
    public void ProFeatures_OnlyOnPro()
    {
        Assert.False(new EditionInfo("Core", "Windows 10 Home", 26300, 1).ProFeaturesEnabled);
        Assert.True(new EditionInfo("Professional", "Windows 10 Pro", 26300, 1).ProFeaturesEnabled);
    }

    [Fact]
    public void Detect_ReadsThisPc() => Assert.False(string.IsNullOrEmpty(EditionInfo.Detect().EditionId));
}

public class AccountFilterTests
{
    private static AccountInfo A(string sid, bool enabled = true) =>
        new(sid, "u", "", AccountType.Standard, enabled, null, null);

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1001", true, true)]
    [InlineData("S-1-5-21-1-2-3-1001", false, false)]
    [InlineData("S-1-5-21-1-2-3-500", true, true)]   // Administrator, only if someone enabled it
    [InlineData("S-1-5-21-1-2-3-500", false, false)]
    [InlineData("S-1-5-21-1-2-3-501", true, false)]  // Guest
    [InlineData("S-1-5-21-1-2-3-503", true, false)]  // DefaultAccount
    [InlineData("S-1-5-21-1-2-3-504", true, false)]  // WDAGUtilityAccount
    public void IsListable(string sid, bool enabled, bool expected) => Assert.Equal(expected, AccountFilter.IsListable(A(sid, enabled)));

    [Theory]
    [InlineData("brother", true)]
    [InlineData("Tomáš", true)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("name@x", false)]
    [InlineData("twentyonecharacters12", false)]
    [InlineData(" lead", false)]
    [InlineData("dot.", false)]
    public void UserNames(string name, bool ok) => Assert.Equal(ok, AccountFilter.IsValidUserName(name));

    [Fact]
    public void Initials()
    {
        Assert.Equal("TN", new AccountInfo("s", "tom", "Tomáš Novák", AccountType.Standard, true, null, null).Initials);
        Assert.Equal("T", new AccountInfo("s", "tom", "", AccountType.Standard, true, null, null).Initials);
    }

    [Fact]
    public void GetAll_ListsCurrentUser()
    {
        var all = LocalAccountService.GetAll();
        Assert.Contains(all, a => a.Sid == LocalAccountService.CurrentUserSid);
    }
}

public class SessionTests
{
    [Fact]
    public void Map_States()
    {
        Assert.Equal(SessionStatus.Active, SessionService.Map(Win32.WTS_CONNECTSTATE_CLASS.WTSActive, Win32.WTS_SESSIONSTATE_UNLOCK));
        Assert.Equal(SessionStatus.Locked, SessionService.Map(Win32.WTS_CONNECTSTATE_CLASS.WTSActive, Win32.WTS_SESSIONSTATE_LOCK));
        Assert.Equal(SessionStatus.Disconnected, SessionService.Map(Win32.WTS_CONNECTSTATE_CLASS.WTSDisconnected, 1));
    }

    [Fact]
    public void StatusFor_PrefersActive()
    {
        var sessions = new[]
        {
            new SessionInfo(2, "bro", "PC", SessionStatus.Disconnected),
            new SessionInfo(3, "bro", "PC", SessionStatus.Active),
        };
        Assert.Equal(SessionStatus.Active, SessionService.StatusFor(sessions, "BRO"));
        Assert.Equal(SessionStatus.SignedOut, SessionService.StatusFor(sessions, "other"));
    }

    [Fact]
    public void GetSessions_FindsThisSession()
    {
        var sessions = SessionService.GetSessions();
        Assert.Contains(sessions, s => s.UserName.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase));
    }
}

public class QuotaTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(10, null, SoftQuotaState.NoLimit)]
    [InlineData(10, 50.0, SoftQuotaState.Ok)]
    [InlineData(39, 50.0, SoftQuotaState.Ok)]
    [InlineData(40, 50.0, SoftQuotaState.Warning)]
    [InlineData(49, 50.0, SoftQuotaState.Warning)]
    [InlineData(50, 50.0, SoftQuotaState.Exceeded)]
    [InlineData(70, 50.0, SoftQuotaState.Exceeded)]
    public void SoftThresholds(long usedGb, double? limit, SoftQuotaState expected) =>
        Assert.Equal(expected, SoftQuota.Evaluate(usedGb * GB, limit));

    [Theory]
    [InlineData("C:", "C:")]
    [InlineData(@"C:\", "C:")]
    [InlineData(@"C:\Users\x", "C:")]
    public void DriveArg(string input, string expected) => Assert.Equal(expected, QuotaAdmin.DriveArg(input));
}

public class PolicyTests
{
    [Fact]
    public void SignInHours_ParseAndCheck()
    {
        Assert.True(SignInHours.TryParse("M-F,08:00-20:00;Sa-Su,10:00-18:00", out var h, out var err), err);
        var monday9 = new DateTime(2026, 11, 9, 9, 0, 0);
        var monday20 = new DateTime(2026, 11, 9, 20, 0, 0);
        var saturday9 = new DateTime(2026, 11, 7, 9, 59, 0);
        var sunday17 = new DateTime(2026, 11, 8, 17, 30, 0);
        Assert.True(h!.IsAllowed(monday9));
        Assert.False(h.IsAllowed(monday20));
        Assert.False(h.IsAllowed(saturday9));
        Assert.True(h.IsAllowed(sunday17));
        Assert.Equal("M-F,08:00-20:00;Sa-Su,10:00-18:00", h.ToNetUserTimes());
    }

    [Theory]
    [InlineData("M,8:00-12:00", "M,08:00-12:00")]
    [InlineData("th,14:00-16:00", "Th,14:00-16:00")]
    public void SignInHours_Normalises(string input, string expected)
    {
        Assert.True(SignInHours.TryParse(input, out var h, out _));
        Assert.Equal(expected, h!.ToNetUserTimes());
    }

    [Theory]
    [InlineData("")]
    [InlineData("M-F,08:30-20:00")]
    [InlineData("M-F,20:00-08:00")]
    [InlineData("F-M,08:00-20:00")]
    [InlineData("M-F,08:00-24:00")]
    [InlineData("Xx,08:00-09:00")]
    [InlineData("M-F 08:00-20:00")]
    public void SignInHours_Rejects(string input) => Assert.False(SignInHours.TryParse(input, out _, out _));

    [Theory]
    [InlineData(10, null, LimitState.NoLimit)]
    [InlineData(10, 60, LimitState.Ok)]
    [InlineData(55, 60, LimitState.WarnSoon)]
    [InlineData(60, 60, LimitState.Reached)]
    public void DailyLimit_Evaluate(int used, int? limit, LimitState expected) => Assert.Equal(expected, DailyLimit.Evaluate(used, limit));

    [Fact]
    public void DailyLimit_ResetsOnNewDay()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0);
        Assert.Equal(("2026-11-07", 31), DailyLimit.AddMinute("2026-11-07", 30, now));
        Assert.Equal(("2026-11-07", 1), DailyLimit.AddMinute("2026-11-06", 300, now));
        Assert.Equal(0, DailyLimit.UsedToday("2026-11-06", 300, now));
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyParser.MOD_CONTROL | HotkeyParser.MOD_ALT, 0x20u, "Ctrl+Alt+Space")]
    [InlineData("win+shift+a", HotkeyParser.MOD_WIN | HotkeyParser.MOD_SHIFT, 0x41u, "Shift+Win+A")]
    [InlineData("Ctrl+F12", HotkeyParser.MOD_CONTROL, 0x7Bu, "Ctrl+F12")]
    public void Parses(string text, uint mods, uint vk, string formatted)
    {
        Assert.True(HotkeyParser.TryParse(text, out var h, out var err), err);
        Assert.Equal(mods, h!.Modifiers);
        Assert.Equal(vk, h.Vk);
        Assert.Equal(formatted, h.Text);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Shift+A")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Nope")]
    public void Rejects(string text) => Assert.False(HotkeyParser.TryParse(text, out _, out _));
}

public class ConfigAndAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AltAccountsTests_" + Guid.NewGuid().ToString("N"));

    public ConfigAndAuditTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void SharedConfig_RoundTrip()
    {
        var c = new SharedConfig { QuotaMode = QuotaMode.Soft };
        c.Shares.Add(new ShareEntry
        {
            Path = @"C:\Shared",
            WarnAtGb = 100,
            Grants = [new ShareGrant { Sid = "S-1-5-21-1-2-3-1001", UserName = "bro", Level = ShareLevel.Modify }],
        });
        c.Policies["S-1-5-21-1-2-3-1001"] = new AccountPolicy { UserName = "bro", DailyLimitMinutes = 120, SignInHours = "M-F,08:00-20:00" };
        string path = Path.Combine(_dir, "config.json");
        ConfigStore.Save(path, c);

        var back = ConfigStore.FixComparer(ConfigStore.Load<SharedConfig>(path));
        Assert.Equal(QuotaMode.Soft, back.QuotaMode);
        Assert.Equal(ShareLevel.Modify, back.Shares[0].Grants[0].Level);
        Assert.Equal(120, back.PolicyFor("s-1-5-21-1-2-3-1001").DailyLimitMinutes); // case-insensitive SID lookup
        Assert.Contains("\"Modify\"", File.ReadAllText(path)); // enums stored as text
    }

    [Fact]
    public void Load_MissingOrCorrupt_GivesDefaults()
    {
        Assert.NotNull(ConfigStore.Load<UserConfig>(Path.Combine(_dir, "nope.json")));
        string bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.Equal(ThemeMode.System, ConfigStore.Load<UserConfig>(bad).Theme);
    }

    [Fact]
    public void Audit_ParseAndClean()
    {
        var e = AuditLog.Parse("2026-11-07T10:00:00.0000000Z\tAlice\tSwitch\tto bro");
        Assert.NotNull(e);
        Assert.Equal("Switch", e!.Action);
        Assert.Null(AuditLog.Parse("garbage"));
        Assert.Equal("a b c", AuditLog.Clean("a\tb\nc"));
    }

    [Fact]
    public void FolderSize_SumsFiles()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllBytes(Path.Combine(_dir, "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_dir, "sub", "b.bin"), new byte[500]);
        Assert.Equal(1500, FolderSize.Measure(_dir));
        Assert.Equal(0, FolderSize.Measure(Path.Combine(_dir, "missing")));
        Assert.Equal("1.5 KB", FolderSize.Format(1536));
    }
}
