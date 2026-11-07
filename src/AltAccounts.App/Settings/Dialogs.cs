using System.Windows;
using System.Windows.Controls;
using AltAccounts.App.Elevation;
using AltAccounts.Core.Accounts;
using AltAccounts.Core.Audit;
using AltAccounts.Core.Sharing;
using Microsoft.Win32;

namespace AltAccounts.App.Settings;

/// <summary>Base for the small code-built dialogs.</summary>
public abstract class DialogBase : Window
{
    protected readonly StackPanel Body = new() { Margin = new Thickness(20) };
    protected readonly TextBlock Error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };

    protected DialogBase(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Error.SetResourceReference(TextBlock.ForegroundProperty, "DangerText");
        Content = Body;
    }

    protected static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 4) };

    protected static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0) };

    protected void AddButtons(string okText, Action onOk)
    {
        Body.Children.Add(Error);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        row.Children.Add(new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) });
        var ok = new Button { Content = okText, IsDefault = true, MinWidth = 120 };
        ok.Click += (_, _) => onOk();
        row.Children.Add(ok);
        Body.Children.Add(row);
    }

    protected void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }
}

public sealed class EditAccountDialog : DialogBase
{
    private readonly TextBox _name;
    private readonly ComboBox _type;
    public string? NewDisplayName { get; private set; }
    public AccountType? NewType { get; private set; }

    public EditAccountDialog(AccountInfo a) : base($"Edit {a.UserName}")
    {
        Body.Children.Add(new TextBlock { Text = $"{a.UserName}", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Body.Children.Add(Label("Display name"));
        _name = new TextBox { Text = a.DisplayName, MaxLength = 64 };
        Body.Children.Add(_name);
        Body.Children.Add(Label("Account type"));
        _type = new ComboBox { Items = { "Standard", "Admin" }, SelectedIndex = a.Type == AccountType.Admin ? 1 : 0 };
        Body.Children.Add(_type);
        Body.Children.Add(Hint("Admin accounts can change settings, install apps and see every file on the PC. Keep a second account Standard unless it really needs more."));
        AddButtons("Save (UAC)", () =>
        {
            NewDisplayName = _name.Text.Trim() == a.DisplayName ? null : _name.Text.Trim();
            var t = _type.SelectedIndex == 1 ? AccountType.Admin : AccountType.Standard;
            NewType = t == a.Type ? null : t;
            DialogResult = true;
        });
    }
}

public sealed class ShareDialog : DialogBase
{
    private readonly TextBox _path;
    private readonly ComboBox _account;
    private readonly ComboBox _level;
    private readonly CheckBox _create;
    private readonly TextBlock _check;
    private readonly SharePathValidator _validator = SharePathValidator.ForThisPc();
    public GrantSharePayload? Result { get; private set; }

    public ShareDialog(IReadOnlyList<AccountInfo> accounts, string? presetPath) : base(presetPath is null ? "Share a folder" : "Give access", 560)
    {
        Body.Children.Add(Label("Folder"));
        var pathRow = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0), IsEnabled = presetPath is null };
        DockPanel.SetDock(browse, Dock.Right);
        _path = new TextBox { Text = presetPath ?? @"C:\Shared", IsReadOnly = presetPath is not null };
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);
        Body.Children.Add(pathRow);
        _check = Hint("");
        Body.Children.Add(_check);
        _create = new CheckBox { Content = "Create the folder if it does not exist", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        Body.Children.Add(_create);

        Body.Children.Add(Label("Account"));
        _account = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Sid" };
        foreach (var a in accounts) _account.Items.Add(new { Label = $"{a.ShownName} ({a.UserName}, {a.TypeLabel})", a.Sid });
        _account.SelectedIndex = accounts.Count > 1 ? 1 : 0;
        if (accounts.FirstOrDefault(a => a.Type == AccountType.Standard) is { } std)
            _account.SelectedIndex = accounts.ToList().IndexOf(std);
        Body.Children.Add(_account);

        Body.Children.Add(Label("Permission"));
        _level = new ComboBox
        {
            Items = { "Read only (can open and play, cannot change files)", "Read and write (can add, change and delete files)", "Full control (also change permissions)" },
            SelectedIndex = 1,
        };
        Body.Children.Add(_level);
        Body.Children.Add(Hint("Applies to every subfolder and file. For a game library that also receives new downloads, \"Read and write\" is usually right."));

        browse.Click += (_, _) =>
        {
            var dlg = new OpenFolderDialog { Title = "Choose a folder to share" };
            if (dlg.ShowDialog(this) == true) _path.Text = dlg.FolderName;
        };
        _path.TextChanged += (_, _) => Validate();
        Validate();

        AddButtons("Share (UAC)", () =>
        {
            var r = _validator.Check(_path.Text);
            if (!r.Ok) { ShowError(r.Error!); return; }
            if (_account.SelectedValue is not string sid) { ShowError("Pick an account."); return; }
            var level = _level.SelectedIndex switch { 0 => ShareLevel.Read, 2 => ShareLevel.Full, _ => ShareLevel.Modify };
            Result = new GrantSharePayload(SharePathValidator.Normalize(_path.Text), sid, level, _create.IsChecked == true);
            DialogResult = true;
        });
    }

    private void Validate()
    {
        var r = _validator.Check(_path.Text);
        _check.Text = r.Ok ? (r.Warning ?? (Directory.Exists(_path.Text) ? "OK." : "OK. The folder does not exist yet.")) : r.Error;
    }
}

public sealed class InputDialog : DialogBase
{
    private readonly TextBox _box;
    public string? Value { get; private set; }

    private InputDialog(string title, string prompt, string initial) : base(title)
    {
        Body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        _box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(_box);
        AddButtons("OK", () => { Value = _box.Text; DialogResult = true; });
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var d = new InputDialog(title, prompt, initial) { Owner = owner };
        return d.ShowDialog() == true ? d.Value : null;
    }
}

/// <summary>Idea 10: switches, sharing changes and admin actions.</summary>
public sealed class AuditLogWindow : Window
{
    public AuditLogWindow()
    {
        Title = "Audit log";
        Width = 900;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = AuditLog.ReadAll(),
            Margin = new Thickness(10),
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new System.Windows.Data.Binding("LocalTime"), Width = 150 });
        grid.Columns.Add(new DataGridTextColumn { Header = "User", Binding = new System.Windows.Data.Binding("User"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Action", Binding = new System.Windows.Data.Binding("Action"), Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Details", Binding = new System.Windows.Data.Binding("Details"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Content = grid;
    }
}
