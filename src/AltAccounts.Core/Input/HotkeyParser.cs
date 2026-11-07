namespace AltAccounts.Core.Input;

/// <summary>Parses "Ctrl+Alt+Space" into RegisterHotKey modifiers and a virtual-key code.</summary>
public static class HotkeyParser
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    public sealed record Hotkey(uint Modifiers, uint Vk, string Text);

    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Escape"] = 0x1B,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Pause"] = 0x13,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
    };

    public static bool TryParse(string? text, out Hotkey? hotkey, out string error)
    {
        hotkey = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "Type a shortcut such as Ctrl+Alt+Space."; return false; }
        uint mods = 0;
        uint? vk = null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; continue;
                case "alt": mods |= MOD_ALT; continue;
                case "shift": mods |= MOD_SHIFT; continue;
                case "win" or "windows": mods |= MOD_WIN; continue;
            }
            if (vk is not null) { error = "Use only one main key."; return false; }
            if (p.Length == 1 && char.IsAsciiLetterOrDigit(p[0])) vk = char.ToUpperInvariant(p[0]);
            else if (p.Length is 2 or 3 && (p[0] is 'F' or 'f') && int.TryParse(p[1..], out int f) && f is >= 1 and <= 24) vk = (uint)(0x70 + f - 1);
            else if (Named.TryGetValue(p, out var n)) vk = n;
            else { error = $"Unknown key \"{p}\"."; return false; }
        }
        if (vk is null) { error = "Add a main key, for example Space."; return false; }
        if (mods == 0) { error = "Add at least one of Ctrl, Alt, Shift or Win."; return false; }
        if (mods == MOD_SHIFT) { error = "Shift alone would block normal typing. Add Ctrl or Alt."; return false; }
        hotkey = new Hotkey(mods, vk.Value, Format(mods, parts.Last()));
        return true;
    }

    private static string Format(uint mods, string key)
    {
        var list = new List<string>();
        if ((mods & MOD_CONTROL) != 0) list.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) list.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) list.Add("Shift");
        if ((mods & MOD_WIN) != 0) list.Add("Win");
        list.Add(key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..]);
        return string.Join('+', list);
    }
}
