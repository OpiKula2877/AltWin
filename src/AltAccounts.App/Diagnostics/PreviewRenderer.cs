#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AltAccounts.App.Popup;
using AltAccounts.App.Settings;
using AltAccounts.Core.Accounts;

namespace AltAccounts.App.Diagnostics;

/// <summary>
/// Debug builds only: "--render-preview &lt;folder&gt;" renders the popup and each Settings tab to PNG,
/// so the UI can be checked without pressing Alt. Windows are shown off-screen. Changes nothing.
/// </summary>
public static class PreviewRenderer
{
    public static async Task<int> RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var host = new AppHost();
        var real = LocalAccountService.GetListable();
        // Add a sample account so the popup shows a typical list on a PC with only one account.
        var sample = new AccountInfo("S-1-5-21-0-0-0-1099", "brother", "Brother", AccountType.Standard, true, null, null);
        host.LoadForPreview(real.Append(sample).ToList());

        var popup = new PopupWindow(host);
        popup.Refresh();
        popup.Left = -6000;
        popup.Top = 100;
        popup.Show();
        popup.UpdateLayout();
        await Task.Delay(300);
        Save(popup.Content as FrameworkElement ?? popup, Path.Combine(folder, "popup.png"));
        popup.Hide();

        var settings = new SettingsWindow(null) { Left = -6000, Top = 100, WindowStartupLocation = WindowStartupLocation.Manual };
        settings.Show();
        await Task.Delay(2500); // let it load accounts
        var tabs = (TabControl)settings.FindName("Tabs");
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            settings.UpdateLayout();
            await Task.Delay(400);
            string name = ((TabItem)tabs.Items[i]).Header.ToString()!.Replace(' ', '-').ToLowerInvariant();
            Save((FrameworkElement)settings.Content, Path.Combine(folder, $"settings-{i + 1}-{name}.png"));
        }
        settings.Close();
        return 0;
    }

    private static void Save(FrameworkElement element, string path)
    {
        double scale = 1.25;
        int w = (int)Math.Ceiling(element.ActualWidth * scale), h = (int)Math.Ceiling(element.ActualHeight * scale);
        var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Fluent windows get their background from DWM (Mica), which a bitmap render cannot capture.
            Brush themeBg = new SolidColorBrush(Services.Theme.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            var own = (element as Control)?.Background ?? (element as Panel)?.Background;
            var bg = own is SolidColorBrush { Color.A: 255 } ? own : themeBg;
            dc.DrawRectangle(bg, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
#endif
