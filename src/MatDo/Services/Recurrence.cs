using System.Globalization;
using MatDo.Localization;

namespace MatDo.Services;

/// <summary>
/// The rules of repeating tasks, as short texts: a pattern, then options after <c>;</c>.
/// <list type="bullet">
/// <item><c>d:1</c> every day, <c>d:3</c> every 3 days, <c>wd</c> every working day (Monday to Friday)</item>
/// <item><c>w:1</c> every week, <c>w:2:1</c> every other week on Monday, <c>w:1:1,4</c> every Monday and Thursday (1 = Monday .. 7 = Sunday)</item>
/// <item><c>m:1</c> every month, <c>m:1:15</c> on the 15th, <c>m:1:L</c> on the last day, <c>m:1:1x1</c> on the first Monday, <c>m:1:Lx5</c> on the last Friday</item>
/// <item><c>y:1</c> every year</item>
/// <item>options: <c>;c</c> counted from the day it is checked off (not from the date it was due), <c>;u=2026-12-31</c> until that day</item>
/// </list>
/// Checking off a repeating task moves its due date to the next occurrence instead of completing it; after the last one (the end date) it is completed.
/// </summary>
public static class Recurrence
{
    /// <summary>A rule read from its text.</summary>
    public sealed record Rule(char Kind, int Every, IReadOnlyList<int> Days, int? MonthDay, bool LastDay, int? Nth, int? NthDay, DateOnly? Until, bool FromDone)
    {
        public override string ToString()
        {
            var text = Kind switch
            {
                'x' => "wd",
                'w' when Days.Count > 0 => $"w:{Every}:{string.Join(',', Days)}",
                'm' when LastDay => $"m:{Every}:L",
                'm' when Nth is not null => $"m:{Every}:{(Nth == -1 ? "L" : Nth.Value.ToString(CultureInfo.InvariantCulture))}x{NthDay}",
                'm' when MonthDay is not null => $"m:{Every}:{MonthDay}",
                _ => $"{Kind}:{Every}",
            };
            if (FromDone)
            {
                text += ";c";
            }

            if (Until is { } u)
            {
                text += ";u=" + Dates.Iso(u);
            }

            return text;
        }
    }

    /// <summary>The rule of a text, or null when it is none.</summary>
    public static Rule? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 60)
        {
            return null;
        }

        var options = text.Split(';');
        DateOnly? until = null;
        var fromDone = false;
        foreach (var o in options.Skip(1))
        {
            if (o == "c")
            {
                fromDone = true;
            }
            else if (o.StartsWith("u=", StringComparison.Ordinal) && Dates.ParseIso(o[2..]) is { } u)
            {
                until = u;
            }
            else
            {
                return null;
            }
        }

        var parts = options[0].Split(':');
        if (parts[0] == "wd")
        {
            return parts.Length == 1 ? new Rule('x', 1, [], null, false, null, null, until, fromDone) : null;
        }

        if (parts.Length is < 2 or > 3 || parts[0].Length != 1 || "dwmy".IndexOf(parts[0][0], StringComparison.Ordinal) < 0
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > 3650)
        {
            return null;
        }

        var kind = parts[0][0];
        if (parts.Length == 2)
        {
            return new Rule(kind, n, [], null, false, null, null, until, fromDone);
        }

        var extra = parts[2];
        if (kind == 'w')
        {
            var days = new SortedSet<int>();
            foreach (var d in extra.Split(','))
            {
                if (!int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day is < 1 or > 7)
                {
                    return null;
                }

                days.Add(day);
            }

            return new Rule(kind, n, days.ToList(), null, false, null, null, until, fromDone);
        }

        if (kind != 'm')
        {
            return null;
        }

        if (extra == "L")
        {
            return new Rule(kind, n, [], null, true, null, null, until, fromDone);
        }

        var x = extra.IndexOf('x', StringComparison.Ordinal);
        if (x > 0)
        {
            int? nth = extra[..x] == "L" ? -1 : int.TryParse(extra[..x], NumberStyles.None, CultureInfo.InvariantCulture, out var k) && k is >= 1 and <= 4 ? k : null;
            if (nth is null || !int.TryParse(extra[(x + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var dow) || dow is < 1 or > 7)
            {
                return null;
            }

            return new Rule(kind, n, [], null, false, nth, dow, until, fromDone);
        }

        return int.TryParse(extra, NumberStyles.None, CultureInfo.InvariantCulture, out var md) && md is >= 1 and <= 31
            ? new Rule(kind, n, [], md, false, null, null, until, fromDone)
            : null;
    }

    public static bool IsValid(string? rule) => Parse(rule) is not null;

    /// <summary>
    /// The first occurrence after <paramref name="from"/> that is also after <paramref name="notBefore"/> (a task that was overdue jumps into the future).
    /// The end date is not looked at here (<see cref="NextDue"/>).
    /// </summary>
    public static DateOnly Next(string rule, DateOnly from, DateOnly notBefore)
        => Next(Parse(rule) ?? throw new FormatException($"'{rule}' is not a rule of a repeating task."), from, notBefore);

    public static DateOnly Next(Rule r, DateOnly from, DateOnly notBefore)
    {
        var limit = from > notBefore ? from : notBefore;
        switch (r.Kind)
        {
            case 'x':
            {
                var d = limit.AddDays(1);
                while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    d = d.AddDays(1);
                }

                return d;
            }

            case 'd':
                return Step(from, limit, d => d.AddDays(r.Every));
            case 'w' when r.Days.Count > 0:
                return NextInWeeks(r, from, limit);
            case 'w':
                return Step(from, limit, d => d.AddDays(7 * r.Every));
            case 'm' when r.MonthDay is not null || r.LastDay || r.Nth is not null:
            {
                var month = new DateOnly(from.Year, from.Month, 1);
                for (var i = 0; i < 2400; i++)
                {
                    var candidate = InMonth(r, month);
                    if (candidate > limit)
                    {
                        return candidate;
                    }

                    month = month.AddMonths(r.Every);
                }

                throw new FormatException($"'{r}' has no next date.");
            }

            case 'm':
                return StepMonths(from, limit, r.Every);
            default:
                return StepMonths(from, limit, 12 * r.Every);
        }
    }

    /// <summary>
    /// The date a repeating task moves to when it is checked off on <paramref name="today"/>: from its due date, or from today when the rule counts from the check-off;
    /// null when that is after the end date (the task is done for good).
    /// </summary>
    public static DateOnly? NextDue(string rule, DateOnly? due, DateOnly today)
    {
        var r = Parse(rule);
        if (r is null)
        {
            return null;
        }

        var from = r.FromDone || due is null ? today : due.Value;
        var next = Next(r, from, today);
        return r.Until is { } until && next > until ? null : next;
    }

    /// <summary>The first day on or after <paramref name="start"/> that fits the rule (a rule bound to weekdays or days of the month), or the start itself.</summary>
    public static DateOnly FirstOnOrAfter(string rule, DateOnly start)
    {
        var r = Parse(rule);
        if (r is null || !(r.Kind == 'x' || r.Days.Count > 0 || r.MonthDay is not null || r.LastDay || r.Nth is not null))
        {
            return start;
        }

        // the first fitting day; the step of n weeks or months then counts from it
        if (r.Kind == 'm')
        {
            var month = new DateOnly(start.Year, start.Month, 1);
            var first = InMonth(r, month);
            return first >= start ? first : InMonth(r, month.AddMonths(1));
        }

        var d = start;
        while (r.Kind == 'x' ? d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday : !r.Days.Contains(Iso(d)))
        {
            d = d.AddDays(1);
        }

        return d;
    }

    /// <summary>The end date of a rule, if it has one.</summary>
    public static DateOnly? Until(string? rule) => Parse(rule)?.Until;

    // the weekdays of the rule, in the weeks n apart from the week of the first of them on or after the due date
    private static DateOnly NextInWeeks(Rule r, DateOnly from, DateOnly limit)
    {
        var first = from;
        while (!r.Days.Contains(Iso(first)))
        {
            first = first.AddDays(1);
        }

        var anchor = MondayOf(first);
        var d = limit.AddDays(1);
        for (var i = 0; i < 7 * r.Every + 7; i++, d = d.AddDays(1))
        {
            var weeks = (MondayOf(d).DayNumber - anchor.DayNumber) / 7;
            if (weeks >= 0 && weeks % r.Every == 0 && r.Days.Contains(Iso(d)))
            {
                return d;
            }
        }

        throw new FormatException($"'{r}' has no next date.");
    }

    // the day of the rule in a month: the n-th day (or the last one when the month is shorter), the last day, the first .. fourth or the last weekday
    private static DateOnly InMonth(Rule r, DateOnly month)
    {
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        if (r.LastDay)
        {
            return month.AddDays(days - 1);
        }

        if (r.Nth is { } nth && r.NthDay is { } dow)
        {
            if (nth == -1)
            {
                var last = month.AddDays(days - 1);
                return last.AddDays(-((Iso(last) - dow + 7) % 7));
            }

            return month.AddDays((dow - Iso(month) + 7) % 7 + 7 * (nth - 1));
        }

        return month.AddDays(Math.Min(r.MonthDay ?? 1, days) - 1);
    }

    private static int Iso(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    private static DateOnly MondayOf(DateOnly d) => d.AddDays(-(Iso(d) - 1));

    private static DateOnly Step(DateOnly from, DateOnly limit, Func<DateOnly, DateOnly> next)
    {
        var d = next(from);
        for (var i = 0; d <= limit && i < 100000; i++)
        {
            d = next(d);
        }

        return d;
    }

    /// <summary>Months keep the day of the first date (the 31st becomes the last day of a shorter month, and comes back in a long one).</summary>
    private static DateOnly StepMonths(DateOnly from, DateOnly limit, int months)
    {
        for (var k = 1; k < 100000; k++)
        {
            var d = from.AddMonths(months * k);
            if (d > limit)
            {
                return d;
            }
        }

        return from.AddMonths(months);
    }

    /// <summary>The rule in words, in the language of the interface.</summary>
    public static string Describe(string? rule)
    {
        var r = Parse(rule);
        if (r is null)
        {
            return rule ?? string.Empty;
        }

        var n = r.Every;
        var text = r.Kind switch
        {
            'x' => Text.T("Recurrence.Weekdays"),
            'd' => n == 1 ? Text.T("Recurrence.Daily") : Text.T("Recurrence.EveryNDays", n),
            'w' when r.Days.Count > 0 => n == 1 ? Text.T("Recurrence.WeeklyOn", DayList(r.Days)) : Text.T("Recurrence.EveryNWeeksOn", n, DayList(r.Days)),
            'w' => n == 1 ? Text.T("Recurrence.Weekly") : Text.T("Recurrence.EveryNWeeks", n),
            'm' when r.LastDay => n == 1 ? Text.T("Recurrence.MonthlyLast") : Text.T("Recurrence.EveryNMonthsLast", n),
            'm' when r.Nth is { } nth => n == 1
                ? Text.T("Recurrence.MonthlyNth", Text.T(nth == -1 ? "Recurrence.Ord.L" : $"Recurrence.Ord.{nth}"), Dates.WeekdayName(r.NthDay!.Value))
                : Text.T("Recurrence.EveryNMonthsNth", n, Text.T(nth == -1 ? "Recurrence.Ord.L" : $"Recurrence.Ord.{nth}"), Dates.WeekdayName(r.NthDay!.Value)),
            'm' when r.MonthDay is { } day => n == 1 ? Text.T("Recurrence.MonthlyOn", day) : Text.T("Recurrence.EveryNMonthsOn", n, day),
            'm' => n == 1 ? Text.T("Recurrence.Monthly") : Text.T("Recurrence.EveryNMonths", n),
            _ => n == 1 ? Text.T("Recurrence.Yearly") : Text.T("Recurrence.EveryNYears", n),
        };
        if (r.FromDone)
        {
            text = Text.T("Recurrence.FromDone", text);
        }

        if (r.Until is { } until)
        {
            text = Text.T("Recurrence.Until", text, Dates.WithYear(until));
        }

        return text;
    }

    private static string DayList(IReadOnlyList<int> days)
    {
        var names = days.Select(Dates.WeekdayName).ToList();
        return names.Count == 1 ? names[0] : Text.T("Recurrence.And", string.Join(", ", names.Take(names.Count - 1)), names[^1]);
    }

    /// <summary>The choices of the edit page.</summary>
    public static IReadOnlyList<(string Value, string Label)> Presets(DateOnly due)
    {
        var dow = Iso(due);
        return
        [
            ("d:1", Describe("d:1")),
            ("wd", Text.T("Recurrence.WeekdaysMonFri")),
            ($"w:1:{dow}", Describe($"w:1:{dow}")),
            ($"w:2:{dow}", Describe($"w:2:{dow}")),
            ($"m:1:{due.Day}", Describe($"m:1:{due.Day}")),
            ("m:1:L", Describe("m:1:L")),
            ("y:1", Describe("y:1")),
        ];
    }

    /// <summary>
    /// The days of a month a monthly rule can take from a date (the part after <c>m:n:</c>): the same day, the last day, the same weekday of the week of the
    /// month (1st..4th) and the last such weekday when the date is in the last week of its month.
    /// </summary>
    public static IReadOnlyList<(string Part, string Label)> MonthParts(DateOnly due)
    {
        var dow = Iso(due);
        var last = due.Day + 7 > DateTime.DaysInMonth(due.Year, due.Month);
        var parts = new List<string> { due.Day.ToString(CultureInfo.InvariantCulture) };
        if (due.Day <= 28)
        {
            parts.Add($"{(due.Day - 1) / 7 + 1}x{dow}");
        }

        if (last)
        {
            parts.Add($"Lx{dow}");
        }

        parts.Add("L");
        return parts.Select(p => (p, Describe("m:1:" + p))).ToList();
    }
}
