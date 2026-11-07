namespace AltAccounts.Core.Policy;

public enum LimitState { NoLimit, Ok, WarnSoon, Reached }

/// <summary>Daily time limit (idea 6). Enforced by the app only, so it is not tamper-proof.</summary>
public static class DailyLimit
{
    public const int WarnMinutesBefore = 5;

    public static LimitState Evaluate(int usedMinutes, int? limitMinutes)
    {
        if (limitMinutes is null or <= 0) return LimitState.NoLimit;
        if (usedMinutes >= limitMinutes) return LimitState.Reached;
        if (usedMinutes >= limitMinutes - WarnMinutesBefore) return LimitState.WarnSoon;
        return LimitState.Ok;
    }

    /// <summary>Adds one minute of use, starting over on a new day.</summary>
    public static (string Date, int Minutes) AddMinute(string? storedDate, int storedMinutes, DateTime now)
    {
        string today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return storedDate == today ? (today, storedMinutes + 1) : (today, 1);
    }

    public static int UsedToday(string? storedDate, int storedMinutes, DateTime now) =>
        storedDate == now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ? storedMinutes : 0;
}
