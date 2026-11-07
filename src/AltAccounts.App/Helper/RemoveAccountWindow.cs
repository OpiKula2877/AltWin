using System.Windows;
using System.Windows.Controls;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Sessions;
using AltAccounts.Core.Storage;

namespace AltAccounts.App.Helper;

/// <summary>
/// Second confirmation for removing an account (the first is in Settings).
/// Shows a deletion preview (idea 19): profile path and size, and whether the account is signed in.
/// </summary>
public sealed class RemoveAccountWindow : Window
{
    private readonly RadioButton _keep = new() { Content = "Keep the profile folder (files stay on disk)", IsChecked = true, Margin = new Thickness(0, 4, 0, 2) };
    private readonly RadioButton _delete = new() { Content = "Delete the profile folder and all its files", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBlock _size = new() { Text = "Measuring…" };
    private readonly CancellationTokenSource _cts = new();

    public bool DeleteProfile => _delete.IsChecked == true;

    public RemoveAccountWindow(AccountInfo acct)
    {
        Title = "Remove account (admin)";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        bool signedIn = SessionService.GetSessions().Any(s => s.UserName.Equals(acct.UserName, StringComparison.OrdinalIgnoreCase));
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"Remove {acct.ShownName} ({acct.UserName})?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(Row("Type", acct.TypeLabel));
        panel.Children.Add(Row("Profile folder", acct.ProfilePath ?? "none (never signed in)"));
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        sizeRow.Children.Add(new TextBlock { Text = "Profile size: ", FontWeight = FontWeights.SemiBold });
        sizeRow.Children.Add(_size);
        panel.Children.Add(sizeRow);

        if (signedIn)
            panel.Children.Add(new TextBlock
            {
                Text = $"{acct.UserName} is signed in right now. They will be signed out first, and anything they have not saved will be lost.",
                Foreground = (System.Windows.Media.Brush)FindResource("DangerText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 4),
            });

        panel.Children.Add(new TextBlock { Text = "Profile folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(_keep);
        panel.Children.Add(_delete);
        panel.Children.Add(new TextBlock
        {
            Text = "The app also removes the folder permissions it gave this account. Removing an account cannot be undone.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) });
        var remove = new Button { Content = "Remove account", MinWidth = 140 };
        remove.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += async (_, _) =>
        {
            if (acct.ProfilePath is null || !Directory.Exists(acct.ProfilePath)) { _size.Text = "0 B"; return; }
            try
            {
                long bytes = await Task.Run(() => FolderSize.Measure(acct.ProfilePath!, _cts.Token));
                _size.Text = FolderSize.Format(bytes);
            }
            catch (OperationCanceledException) { }
        };
        Closed += (_, _) => _cts.Cancel();
    }

    private static UIElement Row(string label, string value)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        sp.Children.Add(new TextBlock { Text = label + ": ", FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        return sp;
    }
}
