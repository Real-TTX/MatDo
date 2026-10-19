using System.Globalization;
using MatDo.Localization;

namespace MatDo.Services;

/// <summary>
/// The rules of repeating tasks, as short texts:
/// <c>d:1</c> every day, <c>d:3</c> every 3 days, <c>wd</c> every working day (Monday to Friday), <c>w:1</c> every week, <c>w:2:1</c> every other week on Monday (1 = Monday .. 7 = Sunday),
/// <c>m:1</c> every month, <c>m:1:15</c> every month on the 15th, <c>y:1</c> every year.
/// Checking off a repeating task moves its due date to the next occurrence instead of completing it.
/// </summary>
public static class Recurrence
{
    public static bool IsValid(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        try
        {
            _ = Next(rule, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The first occurrence after <paramref name="from"/> that is also after <paramref name="notBefore"/> (a task that was overdue jumps into the future).</summary>
    public static DateOnly Next(string rule, DateOnly from, DateOnly notBefore)
    {
        var limit = from > notBefore ? from : notBefore;
        var parts = rule.Split(':');
        if (parts[0] == "wd")
        {
            var d = limit.AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                d = d.AddDays(1);
            }

            return d;
        }

        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > 3650)
        {
            throw new FormatException($"'{rule}' is not a rule of a repeating task.");
        }

        int? extra = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var x) ? x : null;
        switch (parts[0])
        {
            case "d":
                return Step(from, limit, d => d.AddDays(n));
            case "w" when extra is >= 1 and <= 7:
            {
                // the weekday of the rule, in steps of n weeks from the week of the due date
                var dow = extra.Value == 7 ? DayOfWeek.Sunday : (DayOfWeek)extra.Value;
                var start = from.AddDays(((int)dow - (int)from.DayOfWeek + 7) % 7);
                if (start <= from)
                {
                    start = start.AddDays(7 * n);
                }

                return Step(start.AddDays(-7 * n), limit, d => d.AddDays(7 * n));
            }

            case "w":
                return Step(from, limit, d => d.AddDays(7 * n));
            case "m" when extra is >= 1 and <= 31:
            {
                var day = extra.Value;
                var month = new DateOnly(from.Year, from.Month, 1);
                for (var i = 0; i < 2400; i++)
                {
                    var candidate = month.AddDays(Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)) - 1);
                    if (candidate > limit)
                    {
                        return candidate;
                    }

                    month = month.AddMonths(n);
                }

                throw new FormatException($"'{rule}' has no next date.");
            }

            case "m":
                return StepMonths(from, limit, n);
            case "y":
                return StepMonths(from, limit, 12 * n);
            default:
                throw new FormatException($"'{rule}' is not a rule of a repeating task.");
        }
    }

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
        if (string.IsNullOrWhiteSpace(rule))
        {
            return string.Empty;
        }

        var parts = rule.Split(':');
        if (parts[0] == "wd")
        {
            return Text.T("Recurrence.Weekdays");
        }

        var n = parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var v) ? v : 1;
        int? extra = parts.Length > 2 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var x) ? x : null;
        return parts[0] switch
        {
            "d" => n == 1 ? Text.T("Recurrence.Daily") : Text.T("Recurrence.EveryNDays", n),
            "w" when extra is >= 1 and <= 7 => n == 1 ? Text.T("Recurrence.WeeklyOn", Dates.WeekdayName(extra.Value)) : Text.T("Recurrence.EveryNWeeksOn", n, Dates.WeekdayName(extra.Value)),
            "w" => n == 1 ? Text.T("Recurrence.Weekly") : Text.T("Recurrence.EveryNWeeks", n),
            "m" when extra is not null => n == 1 ? Text.T("Recurrence.MonthlyOn", extra) : Text.T("Recurrence.EveryNMonthsOn", n, extra),
            "m" => n == 1 ? Text.T("Recurrence.Monthly") : Text.T("Recurrence.EveryNMonths", n),
            "y" => n == 1 ? Text.T("Recurrence.Yearly") : Text.T("Recurrence.EveryNYears", n),
            _ => rule,
        };
    }

    /// <summary>The choices of the edit page.</summary>
    public static IReadOnlyList<(string Value, string Label)> Presets(DateOnly due)
    {
        var dow = due.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)due.DayOfWeek;
        return
        [
            ("d:1", Describe("d:1")),
            ("wd", Text.T("Recurrence.WeekdaysMonFri")),
            ($"w:1:{dow}", Describe($"w:1:{dow}")),
            ($"w:2:{dow}", Describe($"w:2:{dow}")),
            ($"m:1:{due.Day}", Describe($"m:1:{due.Day}")),
            ("y:1", Describe("y:1")),
        ];
    }
}
