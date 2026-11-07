using System.Windows;
using System.Windows.Controls;
using AltAccounts.App.Elevation;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Config;
using AltAccounts.Core.Edition;
using AltAccounts.Core.Sessions;

namespace AltAccounts.App.Settings;

/// <summary>
/// Settings: Accounts, Shared folders, Storage and quotas, General, About.
/// Runs without admin rights; every change to accounts, folders or shared options goes through
/// the elevated helper, so Windows asks for UAC each time (idea 13).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppHost? _host;
    private readonly EditionInfo _edition;
    private readonly UserConfig _user;
    private readonly string _mySid = LocalAccountService.CurrentUserSid;
    private SharedConfig _shared;
    private List<AccountInfo> _accounts = [];
    private List<SessionInfo> _sessions = [];
    private bool _loaded;

    public SettingsWindow(AppHost? host)
    {
        _host = host;
        _edition = host?.Edition ?? EditionInfo.Detect();
        _user = host?.User ?? ConfigStore.LoadUser();
        _shared = ConfigStore.LoadShared();
        Services.Theme.EnsureFluent();
        InitializeComponent();
        BuildAccentChoices();
        Loaded += async (_, _) =>
        {
            LoadGeneral();
            await ReloadAllAsync();
            _loaded = true;
        };
    }

    private bool IAmAdmin =>
        _accounts.FirstOrDefault(a => a.Sid.Equals(_mySid, StringComparison.OrdinalIgnoreCase))?.Type == AccountType.Admin;

    private async Task ReloadAllAsync()
    {
        SetStatus("Loading…");
        _shared = ConfigStore.LoadShared();
        _host?.ReloadShared();
        try { _accounts = await Task.Run(LocalAccountService.GetListable); }
        catch (Exception ex) { SetStatus("Could not read accounts: " + ex.Message); }
        _sessions = SessionService.GetSessions();
        FillAccounts();
        FillShares();
        FillQuotaMode();
        LoadAbout();
        if (_host is not null) _ = _host.RefreshAccountsAsync();
        SetStatus(IAmAdmin
            ? "Ready. Changes marked (UAC) ask Windows for admin approval."
            : "You are signed in with a standard account. Changes marked (UAC) need an admin's password.");
    }

    private void SetStatus(string text) => StatusText.Text = text;

    /// <summary>Runs an admin operation through the elevated helper and reloads afterwards.</summary>
    private async Task<HelperResult> Elevate(string op, object? payload = null, bool showMessage = true)
    {
        SetStatus("Waiting for admin approval (UAC)…");
        IsEnabled = false;
        HelperResult r;
        try { r = await ElevatedRunner.RunAsync(op, payload); }
        finally { IsEnabled = true; }

        SetStatus(r.Message.Split('\n')[0]);
        if (showMessage && !r.Cancelled)
            MessageBox.Show(this, r.Message, r.Ok ? "Done" : "Not done", MessageBoxButton.OK,
                r.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await ReloadAllAsync();
        if (r.Cancelled) SetStatus(r.Message);
        return r;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAllAsync();

    private bool Confirm(string text, string title = "Please confirm") =>
        MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
