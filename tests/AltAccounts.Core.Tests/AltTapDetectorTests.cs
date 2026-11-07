using AltAccounts.Core.Input;

namespace AltAccounts.Core.Tests;

public class AltTapDetectorTests
{
    private const int LAlt = AltTapDetector.VK_LMENU, RAlt = AltTapDetector.VK_RMENU;
    private const int Tab = 0x09, F4 = 0x73, LCtrl = 0xA2, LShift = 0xA0, KeyA = 0x41, KeyV = 0x56;

    [Fact]
    public void AltAlone_IsTap()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        Assert.True(d.OnKeyUp(LAlt, 120));
    }

    [Fact]
    public void AutoRepeatWhileHeld_StillTap()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        d.OnKeyDown(LAlt, 30);
        d.OnKeyDown(LAlt, 60);
        Assert.True(d.OnKeyUp(LAlt, 200));
    }

    [Theory]
    [InlineData(Tab)]
    [InlineData(F4)]
    [InlineData(KeyA)]
    [InlineData(LShift)] // Alt+Shift = input language switch
    public void AltPlusKey_IsNotTap(int other)
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        d.OnKeyDown(other, 50);
        d.OnKeyUp(other, 80);
        Assert.False(d.OnKeyUp(LAlt, 120));
    }

    [Fact]
    public void AltTab_ThenCleanTap_Works()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        d.OnKeyDown(Tab, 50);
        d.OnKeyUp(Tab, 60);
        Assert.False(d.OnKeyUp(LAlt, 400));
        d.OnKeyDown(LAlt, 1000);
        Assert.True(d.OnKeyUp(LAlt, 1100));
    }

    [Fact]
    public void HeldTooLong_IsNotTap()
    {
        var d = new AltTapDetector { MaxTapMs = 600 };
        d.OnKeyDown(LAlt, 0);
        Assert.False(d.OnKeyUp(LAlt, 900));
    }

    [Fact]
    public void CtrlHeldBeforeAlt_IsNotTap()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LCtrl, 0);
        d.OnKeyDown(LAlt, 20);
        Assert.False(d.OnKeyUp(LAlt, 100));
        d.OnKeyUp(LCtrl, 120);
    }

    [Fact]
    public void AltGr_NeverTaps_ByDefault()
    {
        // AltGr = fake LCtrl down + RAlt down; AltGr+V types "@" on the Czech layout.
        var d = new AltTapDetector();
        d.OnKeyDown(LCtrl, 0);
        d.OnKeyDown(RAlt, 0);
        Assert.False(d.OnKeyUp(RAlt, 80));
        d.OnKeyUp(LCtrl, 80);

        d.OnKeyDown(LCtrl, 200);
        d.OnKeyDown(RAlt, 200);
        d.OnKeyDown(KeyV, 250);
        d.OnKeyUp(KeyV, 270);
        Assert.False(d.OnKeyUp(RAlt, 300));
        d.OnKeyUp(LCtrl, 300);
    }

    [Fact]
    public void RightAlt_AloneIgnoredByDefault_ButCountsWhenEnabled()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(RAlt, 0);
        Assert.False(d.OnKeyUp(RAlt, 80));

        d.IncludeRightAlt = true;
        d.OnKeyDown(RAlt, 200);
        Assert.True(d.OnKeyUp(RAlt, 260));
    }

    [Fact]
    public void AltGrWithRightAltEnabled_StillNotTap_BecauseCtrlIsHeld()
    {
        var d = new AltTapDetector { IncludeRightAlt = true };
        d.OnKeyDown(LCtrl, 0);
        d.OnKeyDown(RAlt, 0);
        Assert.False(d.OnKeyUp(RAlt, 80));
    }

    [Fact]
    public void DoubleTap_GivesTwoTaps()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        Assert.True(d.OnKeyUp(LAlt, 100));
        d.OnKeyDown(LAlt, 300);
        Assert.True(d.OnKeyUp(LAlt, 380));
    }

    [Fact]
    public void MissedKeyUp_IsPruned()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(KeyA, 0); // key-up never arrives (e.g. lost across the lock screen)
        d.PruneHeld(_ => false);
        d.OnKeyDown(LAlt, 100);
        Assert.True(d.OnKeyUp(LAlt, 150));
    }

    [Fact]
    public void Cancel_StopsTap()
    {
        var d = new AltTapDetector();
        d.OnKeyDown(LAlt, 0);
        d.Cancel();
        Assert.False(d.OnKeyUp(LAlt, 100));
    }
}
