using System.Windows;
using System.Windows.Controls;

namespace AltAccounts.App.Helper;

public sealed class ProgressWindow : Window
{
    public ProgressWindow(string text)
    {
        Title = "Alt Account Switcher (admin)";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) },
                new ProgressBar { IsIndeterminate = true, Height = 6 },
            },
        };
    }
}
