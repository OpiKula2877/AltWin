using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using AltAccounts.App.Input;
using AltAccounts.App.Popup;
using AltAccounts.App.Services;
using AltAccounts.App.Settings;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;
using AltAccounts.Core.Edition;
using AltAccounts.Core.Input;
using AltAccounts.Core.Native;
using AltAccounts.Core.Policy;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Sessions;
using AltAccounts.Core.Storage;
using Forms = System.Windows.Forms;

namespace AltAccounts.App;

/// <summary>
/// The background app in one signed-in session: tray icon, Alt trigger, popup,
/// session notifications and the per-account limits (idle lock, daily limit, sign-in hours, soft quota).
/// </summary>
public sealed class AppHost : IDisposable
{
    public EditionInfo Edition { get; } = EditionInfo.Detect();
    public UserConfig User { get; private set; } = new();
    public SharedConfig Shared { get; private set; } = new();
    public List<AccountInfo> Accounts { get; private set; } = [];
    public bool ShuttingDown { get; private set; }
    public string MySid { get; } = LocalAccountService.CurrentUserSid;

    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private KeyboardHook? _hook;
    private MessageWindow? _msg;
    private Forms.NotifyIcon? _tray;
    private PopupWindow? _popup;
    private SettingsWindow? _settings;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;
    private readonly List<DispatcherTimer> _timers = [];
    private readonly Dictionary<int, string> _sessionUsers = [];
    private bool _locked;
    private DateTime? _lockAt;
    private string _lockReason = "";
    private string? _warnedDailyOn;
    private SoftQuotaState _lastQuotaState = SoftQuotaState.NoLimit;
    private readonly Dictionary<string, string> _sizeWarnedOn = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _sharedStamp;
    private int _storageCheckRunning;

    public AccountPolicy MyPolicy => Shared.PolicyFor(MySid);

#if DEBUG
    internal void LoadForPreview(List<AccountInfo> accounts)
    {
        User = ConfigStore.LoadUser();
        ReloadShared();
        Accounts = accounts;
    }
#endif

    public void Start()
    {
        bool firstRun = !File.Exists(AppPaths.UserConfigPath);
        User = ConfigStore.LoadUser();
        ReloadShared();
        try { Accounts = LocalAccountService.GetListable(); } catch (Exception ex) { AuditLog.Append("Error", "Account list: " + ex.Message); }

        _popup = new PopupWindow(this);
        _msg = new MessageWindow();
        _msg.HotkeyPressed += TogglePopup;
        _msg.SessionChanged += OnSessionChanged;
        if (ApplyTrigger() is { } error) Notify("Shortcut not available", error + " Using the Alt tap instead.");

        CreateTray();

        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, App.ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent,
            (_, _) => _ui.BeginInvoke(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);

        foreach (var s in SessionService.GetSessions()) _sessionUsers[s.SessionId] = s.UserName;

        AddTimer(TimeSpan.FromSeconds(15), FastTick);
        AddTimer(TimeSpan.FromMinutes(1), MinuteTick);
        AddTimer(TimeSpan.FromHours(3), () => _ = CheckStorageAsync());
        var firstStorage = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        firstStorage.Tick += (_, _) => { firstStorage.Stop(); _ = CheckStorageAsync(); };
        firstStorage.Start();

        if (firstRun)
        {
            SaveUser();
            Notify("Alt Account Switcher is running",
                AutostartApps.MachineAutostartCommand() is null
                    ? "Tap Alt to switch accounts. It is not set to start with Windows yet; run the installer for that."
                    : "Tap Alt to switch accounts. It starts automatically in every account on this PC.");
        }
    }

    private void AddTimer(TimeSpan interval, Action tick)
    {
        var t = new DispatcherTimer { Interval = interval };
        t.Tick += (_, _) =>
        {
            try { tick(); }
            catch (Exception ex) { AuditLog.Append("Error", ex.Message); }
        };
        t.Start();
        _timers.Add(t);
    }

    // ---------- trigger ----------

    /// <summary>Installs the Alt-tap hook or the custom hotkey. Returns an error text when the hotkey is taken.</summary>
    public string? ApplyTrigger()
    {
        _hook?.Dispose();
        _hook = null;
        _msg!.ClearHotkey();

        string? error = null;
        if (User.Trigger == TriggerMode.Hotkey)
        {
            if (HotkeyParser.TryParse(User.Hotkey, out var hk, out var parseError))
            {
                error = _msg.SetHotkey(hk);
                if (error is null) return null;
            }
            else error = parseError;
        }

        _hook = new KeyboardHook(() => _ui.BeginInvoke(TogglePopup)) { IncludeRightAlt = User.IncludeRightAlt };
        try { _hook.Start(); }
        catch (Exception ex) { error = "Keyboard hook failed: " + ex.Message; }
        return error;
    }

    public void TogglePopup()
    {
        if (_popup is null) return;
        if (_popup.IsOpen) { _popup.Hide(); return; }
        _popup.Open();
        _ = RefreshAccountsAsync();
    }

    public async Task RefreshAccountsAsync()
    {
        try
        {
            var list = await Task.Run(LocalAccountService.GetListable);
            Accounts = list;
            if (_popup?.IsOpen == true) _popup.Refresh();
        }
        catch (Exception ex) { AuditLog.Append("Error", "Account list: " + ex.Message); }
    }

    public void SwitchTo(AccountInfo target, bool direct)
    {
        AuditLog.Append("Switch", $"to {target.UserName}{(direct ? " (sign-in screen)" : "")}");
        if (direct && Edition.ProFeaturesEnabled && SessionService.DisconnectCurrent()) return;
        Win32.LockWorkStation();
    }

    // ---------- windows ----------

    public void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Switch account", null, (_, _) => TogglePopup());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit (this account)", null, (_, _) => Exit());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Alt Account Switcher - tap Alt",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) TogglePopup(); };
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AltAccounts.ico"));
        using var s = info.Stream;
        return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    public void Notify(string title, string text)
    {
        if (_tray is null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(6000);
    }

    public void Exit()
    {
        ShuttingDown = true;
        Application.Current.Shutdown();
    }

    // ---------- config ----------

    public void SaveUser() => ConfigStore.SaveUser(User);

    public void ReloadShared()
    {
        Shared = ConfigStore.LoadShared();
        _sharedStamp = File.Exists(AppPaths.SharedConfigPath) ? File.GetLastWriteTimeUtc(AppPaths.SharedConfigPath) : default;
    }

    private void ReloadSharedIfChanged()
    {
        var stamp = File.Exists(AppPaths.SharedConfigPath) ? File.GetLastWriteTimeUtc(AppPaths.SharedConfigPath) : default;
        if (stamp != _sharedStamp) ReloadShared();
    }

    public bool IAmAdmin => Accounts.FirstOrDefault(a => a.Sid.Equals(MySid, StringComparison.OrdinalIgnoreCase))?.Type == AccountType.Admin;

    /// <summary>Shared folders this account can use (idea 17).</summary>
    public List<string> MyShares() => SharesFor(Shared, MySid, IAmAdmin);

    public static List<string> SharesFor(SharedConfig cfg, string sid, bool isAdmin) =>
        cfg.Shares
            .Where(s => isAdmin || s.Grants.Any(g => g.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.Path)
            .Where(Directory.Exists)
            .ToList();

    // ---------- sessions ----------

    private void OnSessionChanged(int code, int sessionId)
    {
        if (sessionId == SessionService.CurrentSessionId)
        {
            if (code == Win32.WTS_SESSION_LOCK) { _locked = true; _lockAt = null; }
            if (code == Win32.WTS_SESSION_UNLOCK) _locked = false;
            _hook?.ResetState();
            return;
        }

        string Display(string user) =>
            Accounts.FirstOrDefault(a => a.UserName.Equals(user, StringComparison.OrdinalIgnoreCase))?.ShownName ?? user;

        if (code == Win32.WTS_SESSION_LOGON)
        {
            var info = SessionService.Query(sessionId);
            if (info is null || info.UserName.Length == 0) return;
            _sessionUsers[sessionId] = info.UserName;
            if (User.NotifySessionChanges) Notify("Signed in", $"{Display(info.UserName)} signed in on this PC.");
        }
        else if (code == Win32.WTS_SESSION_LOGOFF)
        {
            if (_sessionUsers.Remove(sessionId, out var user) && User.NotifySessionChanges)
                Notify("Signed out", $"{Display(user)} signed out.");
        }
    }

    // ---------- limits ----------

    private void FastTick()
    {
        if (_locked) return;
        var pol = MyPolicy;
        if (pol.IdleLockMinutes is int idle && Win32.GetIdleTime() >= TimeSpan.FromMinutes(idle))
        {
            LockNow("IdleLock", $"idle for {idle} min");
            return;
        }
        if (_lockAt is DateTime at && DateTime.Now >= at)
        {
            _lockAt = null;
            LockNow(_lockReason, "grace minute over");
        }
    }

    private void MinuteTick()
    {
        ReloadSharedIfChanged();
        if (_locked) return;
        var pol = MyPolicy;
        var now = DateTime.Now;

        // Count a minute of use only while someone is actually at the PC (input in the last 5 minutes).
        if (Win32.GetIdleTime() < TimeSpan.FromMinutes(5))
        {
            (User.UsageDate, User.UsageMinutes) = DailyLimit.AddMinute(User.UsageDate, User.UsageMinutes, now);
            SaveUser();
        }

        int used = DailyLimit.UsedToday(User.UsageDate, User.UsageMinutes, now);
        string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        switch (DailyLimit.Evaluate(used, pol.DailyLimitMinutes))
        {
            case LimitState.WarnSoon when _warnedDailyOn != today:
                _warnedDailyOn = today;
                Notify("Time limit", $"{pol.DailyLimitMinutes - used} minute(s) left today. Save your work.");
                break;
            case LimitState.Reached:
                ScheduleLock("DailyLimit", "Today's time limit is used up. The PC locks in 1 minute. Save your work.");
                break;
        }

        if (!string.IsNullOrWhiteSpace(pol.SignInHours) && SignInHours.TryParse(pol.SignInHours, out var hours, out _) && !hours!.IsAllowed(now))
            ScheduleLock("SignInHours", $"This account may be used {pol.SignInHours}. The PC locks in 1 minute. Save your work.");
    }

    private void ScheduleLock(string reason, string message)
    {
        if (_lockAt is not null) return;
        _lockAt = DateTime.Now.AddMinutes(1);
        _lockReason = reason;
        Notify("Locking soon", message);
    }

    private void LockNow(string reason, string detail)
    {
        AuditLog.Append(reason, detail);
        Win32.LockWorkStation();
    }

    /// <summary>Soft quota for this account and folder-size warnings (for admins).</summary>
    private async Task CheckStorageAsync()
    {
        if (Interlocked.Exchange(ref _storageCheckRunning, 1) == 1) return;
        try
        {
            var cfg = Shared;
            var pol = MyPolicy;
            bool admin = IAmAdmin;
            string? profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var myShares = MyShares();
            var warnShares = admin ? cfg.Shares.Where(s => s.WarnAtGb is not null && Directory.Exists(s.Path)).ToList() : [];

            if (cfg.QuotaMode != QuotaMode.Enforced && pol.QuotaGb is not null)
            {
                long used = await Task.Run(() => FolderSize.Measure(profile) + myShares.Sum(p => FolderSize.Measure(p)));
                var state = SoftQuota.Evaluate(used, pol.QuotaGb);
                if (state != _lastQuotaState && state is SoftQuotaState.Warning or SoftQuotaState.Exceeded)
                    Notify(state == SoftQuotaState.Exceeded ? "Disk limit reached" : "Disk limit almost reached",
                        $"This account uses {FolderSize.Format(used)} of its {pol.QuotaGb} GB limit (profile and shared folders). This is a soft limit: nothing is blocked.");
                _lastQuotaState = state;
            }

            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var share in warnShares)
            {
                if (_sizeWarnedOn.TryGetValue(share.Path, out var d) && d == today) continue;
                long size = await Task.Run(() => FolderSize.Measure(share.Path));
                if (FolderSize.ToGb(size) >= share.WarnAtGb)
                {
                    _sizeWarnedOn[share.Path] = today;
                    Notify("Shared folder is large", $"{share.Path} is {FolderSize.Format(size)} (warning set at {share.WarnAtGb} GB).");
                }
            }
        }
        catch (Exception ex) { AuditLog.Append("Error", "Storage check: " + ex.Message); }
        finally { Interlocked.Exchange(ref _storageCheckRunning, 0); }
    }

    public void Dispose()
    {
        ShuttingDown = true;
        foreach (var t in _timers) t.Stop();
        _showSettingsWait?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _hook?.Dispose();
        _msg?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _popup?.Close();
    }
}
