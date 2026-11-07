using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using CoreTheme = AltAccounts.Core.Config.ThemeMode;

namespace AltAccounts.App.Services;

/// <summary>Light and dark themes (idea 9). Uses WPF's Fluent theme for standard controls.</summary>
public static class Theme
{
    public static readonly string[] AccentPalette =
    [
        "#FFB900", "#E74856", "#0099BC", "#00CC6A", "#8764B8", "#FF8C00", "#E3008C", "#2D7D9A", "#10893E", "#767676",
    ];

    public static bool IsDark { get; private set; }

    public static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    private static bool _fluentLoaded;

    /// <summary>
    /// Loads WPF's Fluent theme for standard controls. Called when the first Settings or helper window opens,
    /// so the background app (popup only) does not carry the Fluent resources in memory.
    /// </summary>
    public static void EnsureFluent()
    {
        _fluentLoaded = true;
#pragma warning disable WPF0001 // Fluent ThemeMode is marked experimental
        Application.Current.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
    }

    public static void Apply(CoreTheme mode)
    {
        IsDark = mode == CoreTheme.Dark || (mode == CoreTheme.System && SystemUsesDark());
        if (_fluentLoaded) EnsureFluent();

        Set("PopupBackground", IsDark ? "#163F8F" : "#1E5FD6");
        Set("PopupTitleBar", IsDark ? "#12357A" : "#184FB5");
        Set("PopupRowHover", IsDark ? "#1F4DA8" : "#2A6CE0");
        Set("PopupRowSelected", IsDark ? "#2B5DBF" : "#3B7BEA");
        Set("PopupText", "#FFFFFF");
        Set("PopupSubText", IsDark ? "#B8CBF0" : "#D6E4FF");
        Set("PopupButton", IsDark ? "#E8EEFA" : "#FFFFFF");
        Set("PopupButtonText", IsDark ? "#163F8F" : "#1E5FD6");
        Set("DangerText", IsDark ? "#FF99A4" : "#C42B1C");
        Set("MutedText", IsDark ? "#A0A0A0" : "#6B6B6B");
    }

    private static void Set(string key, string hex) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static Brush AccentBrush(string? hex, int index)
    {
        string h = !string.IsNullOrWhiteSpace(hex) && IsValidColor(hex) ? hex : AccentPalette[Math.Abs(index) % AccentPalette.Length];
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
        b.Freeze();
        return b;
    }

    public static bool IsValidColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { ColorConverter.ConvertFromString(hex); return true; }
        catch (FormatException) { return false; }
    }
}
