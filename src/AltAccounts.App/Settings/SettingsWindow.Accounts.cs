using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AltAccounts.App.Elevation;
using AltAccounts.App.Helper;
using AltAccounts.App.Services;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Config;
using AltAccounts.Core.Quotas;
using AltAccounts.Core.Sessions;

namespace AltAccounts.App.Settings;

public sealed record AccountListRow(AccountInfo Account, string Name, string Type, string Status);

public partial class SettingsWindow
{
    private AccountInfo? SelectedAccount => (AccountsList.SelectedItem as AccountListRow)?.Account;
    private bool IsMe(AccountInfo a) => a.Sid.Equals(_mySid, StringComparison.OrdinalIgnoreCase);

    private void BuildAccentChoices()
    {
        AccentBox.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = null });
        foreach (var hex in Theme.AccentPalette)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Rectangle { Width = 16, Height = 16, RadiusX = 3, RadiusY = 3, Fill = Theme.AccentBrush(hex, 0), Margin = new Thickness(0, 0, 8, 0) });
            sp.Children.Add(new TextBlock { Text = hex });
            AccentBox.Items.Add(new ComboBoxItem { Content = sp, Tag = hex });
        }
        AccentBox.SelectedIndex = 0;
    }

    private void FillAccounts()
    {
        string? keep = SelectedAccount?.Sid;
        var rows = _accounts.Select(a => new AccountListRow(a,
            IsMe(a) ? a.ShownName + " (you)" : a.ShownName,
            a.TypeLabel,
            SessionService.Label(SessionService.StatusFor(_sessions, a.UserName)))).ToList();
        AccountsList.ItemsSource = rows;
        var again = rows.FirstOrDefault(r => r.Account.Sid == keep) ?? rows.FirstOrDefault();
        AccountsList.SelectedItem = again;
        ShowDetails(again?.Account);
    }

    private void AccountsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails(SelectedAccount);

    private void ShowDetails(AccountInfo? a)
    {
        AccountDetails.IsEnabled = a is not null;
        if (a is null)
        {
            DetailName.Text = "Select an account";
            DetailSub.Text = "";
            return;
        }
        var status = SessionService.StatusFor(_sessions, a.UserName);
        var policy = _shared.PolicyFor(a.Sid);
        DetailName.Text = a.ShownName;
        DetailSub.Text = $"{a.UserName} · {a.TypeLabel} · {SessionService.Label(status)}";
        DetailInitials.Text = a.Initials;
        DetailAvatar.Fill = Theme.AccentBrush(policy.AccentColor, _accounts.IndexOf(a));
        DetailPicture.Visibility = Visibility.Collapsed;
        if (a.PicturePath is not null)
        {
            try
            {
                DetailPicture.Fill = new ImageBrush(new BitmapImage(new Uri(a.PicturePath))) { Stretch = Stretch.UniformToFill };
                DetailPicture.Visibility = Visibility.Visible;
            }
            catch (Exception) { }
        }

        RemoveButton.IsEnabled = !IsMe(a);
        SignOutButton.IsEnabled = !IsMe(a) && status != SessionStatus.SignedOut;

        HideBox.IsChecked = policy.HideFromPopup;
        DisableBox.IsChecked = policy.DisableAppHere;
        AccentBox.SelectedItem = AccentBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, policy.AccentColor, StringComparison.OrdinalIgnoreCase)) ?? AccentBox.Items[0];
        IdleBox.Text = policy.IdleLockMinutes?.ToString(CultureInfo.InvariantCulture) ?? "";
        DailyBox.Text = policy.DailyLimitMinutes?.ToString(CultureInfo.InvariantCulture) ?? "";
        HoursBox.Text = policy.SignInHours ?? "";
        QuotaBox.Text = policy.QuotaGb?.ToString(CultureInfo.InvariantCulture) ?? "";
        QuotaHint.Text = _shared.QuotaMode switch
        {
            QuotaMode.Enforced => "Enforced by Windows (NTFS quota on the profile's drive). Counts all files this account owns there, including in shared folders.",
            QuotaMode.Soft => "Soft limit: the app measures the profile and shared folders and warns at 80% and 100%. Nothing is blocked.",
            _ => "Quota support not checked yet. Saving a limit checks it (see Storage and quotas).",
        };
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var r = await Elevate(Ops.AddAccount);
        if (r.Ok && ElevatedRunner.DataAs<SidPayload>(r) is { } created)
            AccountsList.SelectedItem = AccountsList.Items.Cast<AccountListRow>().FirstOrDefault(x => x.Account.Sid == created.Sid);
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a) return;
        var dlg = new EditAccountDialog(a) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await Elevate(Ops.EditAccount, new EditAccountPayload(a.Sid, dlg.NewDisplayName, dlg.NewType));
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a || IsMe(a)) return;
        if (!Confirm($"Remove the account {a.ShownName} ({a.UserName})?\n\nNext, Windows asks for admin approval. Then you see the profile size and choose whether to keep or delete the profile folder.",
                "Remove account (1 of 2)"))
            return;
        await Elevate(Ops.RemoveAccount, new SidPayload(a.Sid));
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a || IsMe(a)) return;
        if (!Confirm($"Sign out {a.ShownName}?\n\nAnything {a.ShownName} has not saved in their open apps will be lost."))
            return;
        await Elevate(Ops.SignOut, new SidPayload(a.Sid));
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a) return;
        if (IsMe(a))
        {
            OfficialPages.Open(OfficialPages.SignInOptions);
            return;
        }
        MessageBox.Show(this,
            $"Windows opens the User Accounts window. Select {a.UserName} and click \"Reset Password\".\n\n" +
            "Resetting a password from another account can make that account lose access to its saved passwords and encrypted files. " +
            $"If possible, let {a.ShownName} change it while signed in: Settings > Accounts > Sign-in options.",
            "Change password", MessageBoxButton.OK, MessageBoxImage.Information);
        OfficialPages.OpenUserAccountsDialog();
    }

    private void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a) return;
        if (IsMe(a)) { OfficialPages.Open(OfficialPages.SignInOptions); return; }
        MessageBox.Show(this,
            $"Only {a.ShownName} can set or change their PIN (Windows Hello), while signed in as themselves:\nSettings > Accounts > Sign-in options > PIN.",
            "Change PIN", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ChangePicture_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a) return;
        if (IsMe(a)) { OfficialPages.Open(OfficialPages.YourInfo); return; }
        MessageBox.Show(this,
            $"Only {a.ShownName} can change their picture, while signed in as themselves:\nSettings > Accounts > Your info.",
            "Change picture", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } a) return;
        if (!TryInt(IdleBox.Text, out int? idle) || !TryInt(DailyBox.Text, out int? daily) || !TryDouble(QuotaBox.Text, out double? quota))
        {
            MessageBox.Show(this, "Use whole numbers for minutes and a number for GB, or leave a box empty.", "Check the options");
            return;
        }
        var policy = _shared.PolicyFor(a.Sid).Clone();
        policy.UserName = a.UserName;
        policy.HideFromPopup = HideBox.IsChecked == true;
        policy.DisableAppHere = DisableBox.IsChecked == true;
        policy.AccentColor = (AccentBox.SelectedItem as ComboBoxItem)?.Tag as string;
        policy.IdleLockMinutes = idle;
        policy.DailyLimitMinutes = daily;
        policy.SignInHours = string.IsNullOrWhiteSpace(HoursBox.Text) ? null : HoursBox.Text.Trim();
        policy.QuotaGb = quota;
        if (HelperOperations.ValidatePolicy(policy) is { } error)
        {
            MessageBox.Show(this, error, "Check the options");
            return;
        }
        await Elevate(Ops.ApplyPolicy, new ApplyPolicyPayload(a.Sid, policy));
    }

    private static bool TryInt(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) { value = v; return true; }
        return false;
    }

    private static bool TryDouble(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string t = text.Trim().Replace(',', '.');
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { value = v; return true; }
        return false;
    }
}
