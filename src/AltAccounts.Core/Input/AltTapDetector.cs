namespace AltAccounts.Core.Input;

/// <summary>
/// Detects a "tap" of Alt: Alt goes down and up with no other key pressed in between,
/// no other key held when Alt went down, and within <see cref="MaxTapMs"/>.
/// Pure state machine so it can be unit-tested; the keyboard hook feeds it.
/// Alt+Tab, Alt+F4, Alt+Shift (language switch) and AltGr never count as a tap.
/// </summary>
public sealed class AltTapDetector
{
    public const int VK_MENU = 0x12;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;

    /// <summary>Right Alt is AltGr on many layouts (Czech, Polish, German), so it is off by default.</summary>
    public bool IncludeRightAlt { get; set; }

    public int MaxTapMs { get; set; } = 600;

    private readonly HashSet<int> _held = [];
    private int _altVk;          // the Alt key currently armed (0 = none)
    private bool _armed;
    private long _downAt;

    private bool IsTriggerAlt(int vk) => vk == VK_LMENU || (IncludeRightAlt && vk == VK_RMENU);

    public void OnKeyDown(int vk, long timeMs)
    {
        if (IsTriggerAlt(vk))
        {
            if (_altVk == vk) return; // auto-repeat while held
            if (_altVk != 0) { _armed = false; _held.Add(vk); return; } // both Alts: not a tap
            _altVk = vk;
            _downAt = timeMs;
            _armed = _held.Count == 0; // e.g. Ctrl held first → Ctrl+Alt, not a tap
            return;
        }
        _held.Add(vk);
        _armed = false; // Alt+anything cancels the tap
    }

    /// <summary>Returns true when this key-up completes a tap.</summary>
    public bool OnKeyUp(int vk, long timeMs)
    {
        if (vk == _altVk && _altVk != 0)
        {
            bool tap = _armed && timeMs - _downAt <= MaxTapMs;
            _altVk = 0;
            _armed = false;
            return tap;
        }
        _held.Remove(vk);
        return false;
    }

    /// <summary>Drops keys whose key-up was missed (for example across a lock screen).</summary>
    public void PruneHeld(Func<int, bool> isStillDown) => _held.RemoveWhere(vk => !isStillDown(vk));

    /// <summary>Called on mouse clicks or when focus state is unknown.</summary>
    public void Cancel() => _armed = false;

    public void Reset()
    {
        _held.Clear();
        _altVk = 0;
        _armed = false;
    }
}
