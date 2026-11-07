using System.Windows;
using AltAccounts.Core.Accounts;

namespace AltAccounts.App.Helper;

/// <summary>Runs only inside the elevated helper, so the password never leaves this process.</summary>
public partial class AddAccountWindow : Window
{
    public string? CreatedSid { get; private set; }
    public string? CreatedUserName { get; private set; }
    public AccountType CreatedType { get; private set; }

    public AddAccountWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UserNameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = UserNameBox.Text.Trim();
        if (!AccountFilter.IsValidUserName(name))
        {
            ShowError("Use 1–20 characters, without \" / \\ [ ] : ; | = , + * ? < > @, and not ending with a dot.");
            return;
        }
        if (PasswordBox1.Password.Length == 0)
        {
            ShowError("Type an initial password.");
            return;
        }
        if (PasswordBox1.Password != PasswordBox2.Password)
        {
            ShowError("The two passwords are different.");
            return;
        }

        var type = TypeBox.SelectedIndex == 1 ? AccountType.Admin : AccountType.Standard;
        try
        {
            CreateButton.IsEnabled = false;
            CreatedSid = AccountAdmin.CreateUser(name, DisplayNameBox.Text, PasswordBox1.Password, type);
            CreatedUserName = name;
            CreatedType = type;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            // Windows' own message, e.g. the password does not meet the policy. Never includes the password.
            ShowError(ex.Message);
            CreateButton.IsEnabled = true;
        }
        finally
        {
            if (CreatedSid is not null)
            {
                PasswordBox1.Clear();
                PasswordBox2.Clear();
            }
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
