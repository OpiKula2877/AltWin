using System.Globalization;
using System.Windows;
using AltAccounts.App.Elevation;
using AltAccounts.App.Services;
using AltAccounts.Core.Sharing;

namespace AltAccounts.App.Settings;

public sealed record ShareRow(ShareEntry Entry, string Path, string Accounts, string Warn);
public sealed record GrantRow(ShareGrant Grant, string UserName, string LevelText, string Added, string Note);

public partial class SettingsWindow
{
    public static string LevelText(ShareLevel l) => l switch
    {
        ShareLevel.Read => "Read only",
        ShareLevel.Modify => "Read and write",
        ShareLevel.Full => "Full control",
        _ => l.ToString(),
    };

    private ShareEntry? SelectedShare => (SharesList.SelectedItem as ShareRow)?.Entry;

    private void FillShares()
    {
        string? keep = SelectedShare?.Path;
        var rows = _shared.Shares.Select(s => new ShareRow(s, s.Path,
            s.Grants.Count == 0 ? "-" : string.Join(", ", s.Grants.Select(g => $"{g.UserName} ({LevelText(g.Level)})")),
            s.WarnAtGb is null ? "Off" : $"{s.WarnAtGb} GB")).ToList();
        SharesList.ItemsSource = rows;
        SharesList.SelectedItem = rows.FirstOrDefault(r => r.Path == keep) ?? rows.FirstOrDefault();
        FillGrants();
    }

    private void SharesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => FillGrants();

    private void FillGrants()
    {
        GrantsList.ItemsSource = SelectedShare?.Grants.Select(g => new GrantRow(g, g.UserName, LevelText(g.Level),
            g.GrantedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            g.PreExisting ? "Had its own permission before; that is never removed" : "")).ToList();
    }

    private async void ShareFolder_Click(object sender, RoutedEventArgs e) => await ShareWithDialog(null);

    private async void AddGrant_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedShare is not { } s) { MessageBox.Show(this, "Select a shared folder first, or use \"Share a folder…\".", "Shared folders"); return; }
        await ShareWithDialog(s.Path);
    }

    private async Task ShareWithDialog(string? path)
    {
        var dlg = new ShareDialog(_accounts, path) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        await Elevate(Ops.GrantShare, dlg.Result);
    }

    private async void RemoveGrant_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedShare is not { } s || (GrantsList.SelectedItem as GrantRow)?.Grant is not { } g)
        {
            MessageBox.Show(this, "Select an account in the lower list first.", "Shared folders");
            return;
        }
        if (!Confirm($"Remove {g.UserName}'s access to {s.Path}?")) return;
        await Elevate(Ops.RemoveShare, new RemoveSharePayload(s.Path, g.Sid));
    }

    private async void RemoveShare_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedShare is not { } s) return;
        if (!Confirm($"Stop sharing {s.Path}?\n\nThe app removes the permissions it added. The folder and its files stay where they are.")) return;
        await Elevate(Ops.RemoveShare, new RemoveSharePayload(s.Path, null));
    }

    private async void ShareWarn_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedShare is not { } s) return;
        var input = InputDialog.Ask(this, "Size warning",
            $"Warn admins when {s.Path} is larger than this many GB. Leave empty to turn the warning off.",
            s.WarnAtGb?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (input is null) return;
        double? gb = null;
        if (input.Trim().Length > 0)
        {
            if (!double.TryParse(input.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0)
            {
                MessageBox.Show(this, "Enter a number of GB greater than 0.", "Size warning");
                return;
            }
            gb = v;
        }
        await Elevate(Ops.SetShareWarn, new ShareWarnPayload(s.Path, gb), showMessage: false);
    }

    private void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedShare is { } s) OfficialPages.OpenFolder(s.Path);
    }

    private async void Stale_Click(object sender, RoutedEventArgs e)
    {
        var items = StaleScan.Find(_shared);
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Nothing stale: every shared folder and account still exists.", "Stale shares");
            return;
        }
        if (!Confirm("Found:\n\n• " + string.Join("\n• ", items.Select(i => i.Description)) + "\n\nRemove these entries (and the app's permissions where the folder still exists)?",
                "Stale shares"))
            return;
        await Elevate(Ops.CleanupStale);
    }
}
