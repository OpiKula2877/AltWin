using System.Globalization;
using System.Text.RegularExpressions;

namespace AltAccounts.Core.Policy;

/// <summary>
/// Allowed sign-in hours, written in the same 24-hour syntax that "net user /times:" accepts:
/// "M-F,08:00-20:00;Sa-Su,10:00-18:00". Whole hours only (a Windows rule).
/// Windows enforces this for new sign-ins; the app additionally locks open sessions outside the hours.
/// </summary>
public sealed partial class SignInHours
{
    // Monday-first order, as net user shows it.
    private static readonly string[] Days = ["M", "T", "W", "Th", "F", "Sa", "Su"];

    public sealed record Rule(int FromDay, int ToDay, int StartHour, int EndHour);

    public IReadOnlyList<Rule> Rules { get; }

    private SignInHours(List<Rule> rules) => Rules = rules;

    [GeneratedRegex(@"^(?<d1>Th|Sa|Su|M|T|W|F)(-(?<d2>Th|Sa|Su|M|T|W|F))?,(?<h1>\d{1,2}):00-(?<h2>\d{1,2}):00$", RegexOptions.IgnoreCase)]
    private static partial Regex RulePattern();

    public static bool TryParse(string? text, out SignInHours? hours, out string error)
    {
        hours = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = "Empty."; return false; }
        var rules = new List<Rule>();
        foreach (var raw in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = RulePattern().Match(raw);
            if (!m.Success) { error = $"\"{raw}\" should look like M-F,08:00-20:00."; return false; }
            int d1 = DayIndex(m.Groups["d1"].Value);
            int d2 = m.Groups["d2"].Success ? DayIndex(m.Groups["d2"].Value) : d1;
            int h1 = int.Parse(m.Groups["h1"].Value, CultureInfo.InvariantCulture);
            int h2 = int.Parse(m.Groups["h2"].Value, CultureInfo.InvariantCulture);
            if (d2 < d1) { error = $"\"{raw}\": day ranges run Monday to Sunday (for example Sa-Su)."; return false; }
            if (h1 is < 0 or > 23 || h2 is < 1 or > 23 || h2 <= h1)
            { error = $"\"{raw}\": use whole hours from 00:00 to 23:00, start before end."; return false; }
            rules.Add(new Rule(d1, d2, h1, h2));
        }
        if (rules.Count == 0) { error = "Empty."; return false; }
        hours = new SignInHours(rules);
        return true;
    }

    private static int DayIndex(string s) =>
        Array.FindIndex(Days, d => d.Equals(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Monday = 0 … Sunday = 6.</summary>
    public static int MondayIndex(DayOfWeek d) => ((int)d + 6) % 7;

    public bool IsAllowed(DateTime localTime)
    {
        int day = MondayIndex(localTime.DayOfWeek);
        int hour = localTime.Hour;
        return Rules.Any(r => day >= r.FromDay && day <= r.ToDay && hour >= r.StartHour && hour < r.EndHour);
    }

    /// <summary>The value for "net user name /times:VALUE".</summary>
    public string ToNetUserTimes() => string.Join(';', Rules.Select(r =>
        (r.FromDay == r.ToDay ? Days[r.FromDay] : $"{Days[r.FromDay]}-{Days[r.ToDay]}") +
        $",{r.StartHour:00}:00-{r.EndHour:00}:00"));

    public override string ToString() => ToNetUserTimes();
}
