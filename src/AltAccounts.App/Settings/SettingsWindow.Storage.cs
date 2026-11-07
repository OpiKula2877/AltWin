using System.Globalization;
using System.Windows;
using AltAccounts.App.Elevation;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Storage;

namespace AltAccounts.App.Settings;

public sealed record StorageListRow(string Name, string Profile, string Shared, string Limit, string State);

public partial class SettingsWindow
{
    private void FillQuotaMode()
    {
        QuotaModeText.Text = _shared.QuotaMode switch
        {
            QuotaMode.Enforced => "Enforced (NTFS disk quotas)",
            QuotaMode.Soft => "Soft limit (warnings only)",
            _ => "Not checked yet",
        };
        string when = _shared.QuotaCheckedUtc is { } t ? $" Checked {t.ToLocalTime():yyyy-MM-dd HH:mm}." : "";
        string detail = string.IsNullOrWhiteSpace(_shared.QuotaDetail) ? "" : $" fsutil: {_shared.QuotaDetail}";
        QuotaModeDetail.Text = SoftQuota.Describe(_shared.QuotaMode) + when + detail;
    }

    private async void ProbeQuota_Click(object sender, RoutedEventArgs e) => await Elevate(Ops.ProbeQuota);

    private async void MeasureAll_Click(object sender, RoutedEventArgs e)
    {
        var r = await Elevate(Ops.MeasureStorage, showMessage: false);
        if (!r.Ok) { if (!r.Cancelled) MessageBox.Show(this, r.Message, "Not done"); return; }
        var rows = ElevatedRunner.DataAs<List<StorageRow>>(r) ?? [];
        StorageList.ItemsSource = rows.Select(x => ToRow(x.DisplayName, x.ProfileBytes, x.SharedBytes, x.QuotaGb)).ToList();
        SetStatus($"Measured {rows.Count} account(s).");
    }

    private async void MeasureMine_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Measuring your profile…");
        var me = _accounts.FirstOrDefault(a => a.Sid.Equals(_mySid, StringComparison.OrdinalIgnoreCase));
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var shares = AppHost.SharesFor(_shared, _mySid, me?.Type == AccountType.Admin);
        var (p, s) = await Task.Run(() => (FolderSize.Measure(profile), shares.Sum(x => FolderSize.Measure(x))));
        StorageList.ItemsSource = new[] { ToRow(me?.ShownName ?? Environment.UserName, p, s, _shared.PolicyFor(_mySid).QuotaGb) };
        SetStatus("Measured your account. Other profiles need admin rights to read.");
    }

    private StorageListRow ToRow(string name, long profile, long shared, double? quotaGb)
    {
        string state;
        if (quotaGb is null) state = "No limit";
        else if (_shared.QuotaMode == QuotaMode.Enforced) state = "Enforced by Windows (counts files owned on the drive)";
        else state = SoftQuota.Evaluate(profile + shared, quotaGb) switch
        {
            SoftQuotaState.Exceeded => "Over the limit (warning only)",
            SoftQuotaState.Warning => "Above 80% (warning only)",
            _ => "OK",
        };
        return new StorageListRow(name, FolderSize.Format(profile), FolderSize.Format(shared),
            quotaGb is null ? "-" : quotaGb.Value.ToString(CultureInfo.InvariantCulture) + " GB", state);
    }
}
