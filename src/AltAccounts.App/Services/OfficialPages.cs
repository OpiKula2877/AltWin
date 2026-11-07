using System.Diagnostics;
using System.Windows;

namespace AltAccounts.App.Services;

/// <summary>
/// Official Windows pages. The app never sees, stores or changes a password or PIN itself.
/// </summary>
public static class OfficialPages
{
    public const string SignInOptions = "ms-settings:signinoptions";   // own password, PIN (Windows Hello)
    public const string YourInfo = "ms-settings:yourinfo";             // own picture
    public const string OtherUsers = "ms-settings:otherusers";         // other accounts

    public static void Open(string target, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {target}: {ex.Message}", "Alt Account Switcher");
        }
    }

    /// <summary>netplwiz: "User Accounts" dialog, where an admin can reset another local account's password.</summary>
    public static void OpenUserAccountsDialog() => Open("netplwiz.exe");

    public static void OpenFolder(string path) => Open("explorer.exe", $"\"{path}\"");
}
