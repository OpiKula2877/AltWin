using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AltAccounts.App.Services;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Native;
using AltAccounts.Core.Sessions;

namespace AltAccounts.App.Popup;

public sealed class AccountRow
{
    public required AccountInfo Account { get; init; }
    public required string Number { get; init; }
    public required string Name { get; init; }
    public required string Details { get; init; }
    public required string Initials { get; init; }
    public required Brush Accent { get; init; }
    public ImageSource? Picture { get; init; }
    public Visibility PictureVisibility => Picture is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ProVisibility { get; init; } = Visibility.Collapsed;
}

/// <summary>The blue account popup. Borderless with a custom title bar and rounded corners.</summary>
public partial class PopupWindow : Window
{
    private readonly AppHost _host;
    private readonly bool _useDwmCorners;
    private AccountRow? _pendingSwitch;
    private bool _pendingDirect;

    public PopupWindow(AppHost host)
    {
        _host = host;
        _useDwmCorners = host.Edition.IsWindows11;
        if (!_useDwmCorners)
        {
            // Windows 10: no DWM corner preference. Use a transparent window with a clipped, rounded Border.
            AllowsTransparency = true;
        }
        InitializeComponent();
        if (!_useDwmCorners)
        {
            Background = Brushes.Transparent;
            RootBorder.CornerRadius = new CornerRadius(12);
        }
        SourceInitialized += (_, _) =>
        {
            if (!_useDwmCorners) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            int pref = Win32.DWMWCP_ROUND;
            Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        };
    }

    public bool IsOpen => IsVisible;

    public void Open()
    {
        Refresh();
        HintPanel.Visibility = Visibility.Collapsed;
        _pendingSwitch = null;
        Show();
        // Centre on the primary work area.
        var wa = SystemParameters.WorkArea;
        UpdateLayout();
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Top + (wa.Height - ActualHeight) / 2.4;
        Activate();
        Focus();
        if (AccountList.Items.Count > 0)
        {
            AccountList.SelectedIndex = 0;
            Dispatcher.BeginInvoke(() =>
            {
                if (AccountList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
            });
        }
    }

    public void Refresh()
    {
        var sessions = SessionService.GetSessions();
        var shared = _host.Shared;
        var mySid = LocalAccountService.CurrentUserSid;
        bool pro = _host.Edition.ProFeaturesEnabled;
        var rows = new List<AccountRow>();
        int index = 0;
        foreach (var a in _host.Accounts)
        {
            if (a.Sid.Equals(mySid, StringComparison.OrdinalIgnoreCase)) { index++; continue; }
            var policy = shared.PolicyFor(a.Sid);
            if (policy.HideFromPopup) { index++; continue; }
            var status = SessionService.StatusFor(sessions, a.UserName);
            int n = rows.Count + 1;
            rows.Add(new AccountRow
            {
                Account = a,
                Number = n <= 9 ? n.ToString() : "",
                Name = a.ShownName,
                Details = $"{a.UserName} · {a.TypeLabel} · {SessionService.Label(status)}",
                Initials = a.Initials,
                Accent = Theme.AccentBrush(policy.AccentColor, index),
                Picture = LoadPicture(a.PicturePath),
                ProVisibility = pro ? Visibility.Visible : Visibility.Collapsed,
            });
            index++;
        }
        AccountList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SharedButton.IsEnabled = _host.MyShares().Count > 0;
        SharedButton.ToolTip = SharedButton.IsEnabled ? "Open a shared folder" : "No shared folders for this account";
    }

    private static ImageSource? LoadPicture(string? path)
    {
        if (path is null) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 80;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception) { return null; }
    }

    // ---- switching ----

    private void RequestSwitch(AccountRow row, bool direct)
    {
        if (!_host.User.SeenSwitchHint)
        {
            _pendingSwitch = row;
            _pendingDirect = direct;
            HintPanel.Visibility = Visibility.Visible;
            return;
        }
        DoSwitch(row, direct);
    }

    private void DoSwitch(AccountRow row, bool direct)
    {
        Hide();
        _host.SwitchTo(row.Account, direct);
    }

    private void HintOk_Click(object sender, RoutedEventArgs e)
    {
        _host.User.SeenSwitchHint = true;
        _host.SaveUser();
        HintPanel.Visibility = Visibility.Collapsed;
        if (_pendingSwitch is { } row) DoSwitch(row, _pendingDirect);
    }

    private void HintCancel_Click(object sender, RoutedEventArgs e)
    {
        HintPanel.Visibility = Visibility.Collapsed;
        _pendingSwitch = null;
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AccountRow row) RequestSwitch(row, direct: false);
    }

    private void Direct_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AccountRow row) RequestSwitch(row, direct: true);
    }

    private void AccountList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AccountList.SelectedItem is AccountRow row) RequestSwitch(row, direct: false);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (HintPanel.Visibility == Visibility.Visible) HintCancel_Click(this, e);
                else Hide();
                e.Handled = true;
                return;
            case Key.Enter:
                if (HintPanel.Visibility == Visibility.Visible) HintOk_Click(this, e);
                else if (AccountList.SelectedItem is AccountRow row) RequestSwitch(row, direct: false);
                e.Handled = true;
                return;
        }

        int digit = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (digit > 0 && AccountList.ItemsSource is List<AccountRow> rows && digit <= rows.Count)
        {
            AccountList.SelectedIndex = digit - 1;
            RequestSwitch(rows[digit - 1], direct: false);
            e.Handled = true;
        }
    }

    // ---- chrome ----

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // The popup has no taskbar button; "minimize" tucks it away like closing. Alt brings it back.
    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible) Hide();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _host.ShowSettings();
    }

    private void Shared_Click(object sender, RoutedEventArgs e)
    {
        var shares = _host.MyShares();
        if (shares.Count == 1)
        {
            Hide();
            OfficialPages.OpenFolder(shares[0]);
            return;
        }
        var menu = new ContextMenu { PlacementTarget = SharedButton };
        foreach (var path in shares)
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => { Hide(); OfficialPages.OpenFolder(path); };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Alt+F4 or similar: keep the window for reuse.
        if (!_host.ShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
