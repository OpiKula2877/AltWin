using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AltAccounts.App.Elevation;
using AltAccounts.App.Services;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Config;
using AltAccounts.Core.Input;
using AltAccounts.Core.Native;
using AltAccounts.Core.Quotas;
using Microsoft.Win32;
using CoreTheme = AltAccounts.Core.Config.ThemeMode;

namespace AltAccounts.App.Settings;

public partial class SettingsWindow
{
    private void LoadGeneral()
    {
        AltTapRadio.IsChecked = _user.Trigger == TriggerMode.AltTap;
        HotkeyRadio.IsChecked = _user.Trigger == TriggerMode.Hotkey;
        HotkeyBox.Text = _user.Hotkey ?? "Ctrl+Alt+Space";
        RightAltBox.IsChecked = _user.IncludeRightAlt;
        ThemeBox.SelectedIndex = _user.Theme switch { CoreTheme.Light => 1, CoreTheme.Dark => 2, _ => 0 };
        NotifyBox.IsChecked = _user.NotifySessionChanges;
        TriggerStatus.Text = _host is null ? "The background app is not running in this account." : "";

        string? cmd = AutostartApps.MachineAutostartCommand();
        AutostartText.Text = cmd is null
            ? "Not set. The installer adds one machine-wide entry so the app starts in every account; you can then turn it off per account in Accounts → Options."
            : "Starts automatically in every account on this PC (one machine-wide entry made by the installer). An admin can turn it off per account in Accounts → Options.";

        RefreshDownloads();
        RefreshAutoApps();
    }

    private void ApplyTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyRadio.IsChecked == true)
        {
            if (!HotkeyParser.TryParse(HotkeyBox.Text, out var hk, out var err)) { TriggerStatus.Text = err; return; }
            _user.Trigger = TriggerMode.Hotkey;
            _user.Hotkey = hk!.Text;
            HotkeyBox.Text = hk.Text;
        }
        else _user.Trigger = TriggerMode.AltTap;
        _user.IncludeRightAlt = RightAltBox.IsChecked == true;
        ConfigStore.SaveUser(_user);

        if (_host is null) { TriggerStatus.Text = "Saved. It takes effect when the background app starts."; return; }
        var error = _host.ApplyTrigger();
        TriggerStatus.Text = error is null
            ? (_user.Trigger == TriggerMode.Hotkey ? $"Active: {_user.Hotkey}." : "Active: tap Alt.")
            : error + " The Alt tap stays active instead.";
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _user.Theme = ThemeBox.SelectedIndex switch { 1 => CoreTheme.Light, 2 => CoreTheme.Dark, _ => CoreTheme.System };
        ConfigStore.SaveUser(_user);
        Theme.Apply(_user.Theme);
    }

    private void NotifyBox_Click(object sender, RoutedEventArgs e)
    {
        _user.NotifySessionChanges = NotifyBox.IsChecked == true;
        ConfigStore.SaveUser(_user);
    }

    // ---- Downloads (idea 18) ----

    private void RefreshDownloads()
    {
        string? current = Win32.GetKnownFolderPath(Win32.FOLDERID_Downloads);
        DownloadsText.Text = "Now: " + (current ?? "unknown");
    }

    private void DownloadsShare_Click(object sender, RoutedEventArgs e)
    {
        var shares = AppHost.SharesFor(_shared, _mySid, IAmAdmin);
        if (shares.Count == 0) { MessageBox.Show(this, "No shared folders are available to your account.", "Downloads"); return; }
        var menu = new ContextMenu { PlacementTarget = DownloadsShareButton };
        foreach (var p in shares)
        {
            var item = new MenuItem { Header = p };
            item.Click += (_, _) => SetDownloads(p);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void DownloadsChoose_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose your Downloads folder" };
        if (dlg.ShowDialog(this) == true) SetDownloads(dlg.FolderName);
    }

    private void DownloadsDefault_Click(object sender, RoutedEventArgs e) =>
        SetDownloads(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

    private void SetDownloads(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Win32.SetKnownFolderPath(Win32.FOLDERID_Downloads, path);
            AuditLog.Append("SetDownloads", path);
            RefreshDownloads();
            SetStatus("Downloads folder changed. Files already downloaded were not moved.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not change the Downloads folder: " + ex.Message, "Downloads");
        }
    }

    // ---- Autostart apps (idea 16) ----

    private void RefreshAutoApps() => AutoAppsList.ItemsSource = AutostartApps.List();

    private void AddAutoApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Choose an app to start when you sign in", Filter = "Programs (*.exe)|*.exe" };
        if (dlg.ShowDialog(this) != true) return;
        AutostartApps.Add(dlg.FileName);
        AuditLog.Append("AddAutostart", dlg.FileName);
        RefreshAutoApps();
    }

    private void RemoveAutoApp_Click(object sender, RoutedEventArgs e)
    {
        if (AutoAppsList.SelectedItem is not AutostartApps.Entry entry) return;
        AutostartApps.Remove(entry.ValueName);
        AuditLog.Append("RemoveAutostart", entry.Command);
        RefreshAutoApps();
    }

    // ---- Export / import (idea 12), audit log (idea 10), updates (idea 20) ----

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Export settings", Filter = "JSON (*.json)|*.json", FileName = "AltAccounts-settings.json" };
        if (dlg.ShowDialog(this) != true) return;
        ConfigStore.Save(dlg.FileName, new ExportBundle { Shared = _shared });
        SetStatus("Exported to " + dlg.FileName + ". Accounts are matched by username when you import on another PC.");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Import settings", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        var bundle = ConfigStore.FixComparerBundle(ConfigStore.Load<ExportBundle>(dlg.FileName));
        int grants = bundle.Shared.Shares.Sum(s => s.Grants.Count);
        if (!Confirm($"Import from {bundle.MachineName} ({bundle.ExportedUtc.ToLocalTime():yyyy-MM-dd})?\n\n" +
                     $"{bundle.Shared.Policies.Count} account option set(s), {bundle.Shared.Shares.Count} shared folder(s) with {grants} permission(s).\n" +
                     "Accounts are matched by username. Folders that do not exist here are skipped.", "Import settings"))
            return;
        await Elevate(Ops.Import, new ImportPayload(bundle));
    }

    private void AuditLog_Click(object sender, RoutedEventArgs e) => new AuditLogWindow { Owner = this }.Show();

    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        UpdateText.Text = "Checking…";
        UpdateText.Text = await UpdateCheck.CheckAsync();
    }

    // ---- About ----

    private void LoadAbout()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {v?.ToString(3)} · {Environment.ProcessPath}";
        EditionText.Text = _edition.DisplayName;
        AboutQuotaText.Text = $"{_shared.QuotaMode}. {SoftQuota.Describe(_shared.QuotaMode)}";
        ProText.Text = _edition.ProFeaturesEnabled
            ? "On: the popup also offers \"Sign-in screen\", which shows the Windows sign-in screen without locking first. The password is still required."
            : "Off: this PC does not run Windows Pro, so Switch locks the PC and you pick the account on the sign-in screen.";
        ConfigText.Text = ProgramDataSetup.Exists
            ? $"{AppPaths.SharedConfigPath} (only admins can change it)"
            : $"{AppPaths.SharedConfigPath} does not exist yet. It is created the first time an admin saves a change (or by the installer).";
    }
}
