using System.Globalization;
using MatDo.Localization;

namespace MatDo.Services;

/// <summary>Dates as a person reads them, in the language of the interface (English or German), and "today" in the time zone of a person.</summary>
public static class Dates
{
    private static readonly string[] WeekdaysDe = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
    private static readonly string[] WeekdaysShortDe = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."];
    private static readonly string[] MonthsDe = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
    private static readonly string[] MonthsShortDe = ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sep.", "Okt.", "Nov.", "Dez."];

    private static readonly string[] WeekdaysEn = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] WeekdaysShortEn = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthsEn = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] MonthsShortEn = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static bool De => Languages.IsGerman;

    private static string[] Weekdays => De ? WeekdaysDe : WeekdaysEn;

    private static string[] WeekdaysShort => De ? WeekdaysShortDe : WeekdaysShortEn;

    private static string[] Months => De ? MonthsDe : MonthsEn;

    private static string[] MonthsShort => De ? MonthsShortDe : MonthsShortEn;

    /// <summary>1 = Monday .. 7 = Sunday.</summary>
    public static string WeekdayName(int iso) => Weekdays[iso % 7];

    public static string WeekdayName(DayOfWeek dow) => Weekdays[(int)dow];

    public static TimeZoneInfo Zone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            return zone;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Berlin", out var berlin) ? berlin : TimeZoneInfo.Utc;
    }

    public static bool IsZone(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static DateTime Local(DateTime utc, string? zone) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(zone));

    public static DateOnly Today(string? zone) => DateOnly.FromDateTime(Local(DateTime.UtcNow, zone));

    public static TimeOnly Now(string? zone) => TimeOnly.FromDateTime(Local(DateTime.UtcNow, zone));

    /// <summary>"Today", "Tomorrow", "Yesterday", the weekday within the next six days, else "24 Oct" / "24. Okt." (with the year when it is another one).</summary>
    public static string Relative(DateOnly date, DateOnly today)
    {
        var diff = date.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => Text.T("Common.Date.Today"),
            1 => Text.T("Common.Date.Tomorrow"),
            2 => Text.T("Common.Date.DayAfterTomorrow"),
            -1 => Text.T("Common.Date.Yesterday"),
            > 2 and < 7 => Weekdays[(int)date.DayOfWeek],
            _ => date.Year == today.Year ? DayMonth(date) : $"{DayMonth(date)} {date.Year}",
        };
    }

    public static string Relative(DateOnly date, TimeOnly? time, DateOnly today)
        => time is null ? Relative(date, today) : $"{Relative(date, today)} {Time(time.Value)}";

    private static string DayMonth(DateOnly date) => De ? $"{date.Day}. {MonthsShort[date.Month - 1]}" : $"{date.Day} {MonthsShort[date.Month - 1]}";

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Monday, 20 October" / "Montag, 20. Oktober" (the head of a day).</summary>
    public static string Long(DateOnly date, bool withYear = false)
        => (De ? $"{Weekdays[(int)date.DayOfWeek]}, {date.Day}. {Months[date.Month - 1]}" : $"{Weekdays[(int)date.DayOfWeek]}, {date.Day} {Months[date.Month - 1]}")
           + (withYear ? $" {date.Year}" : string.Empty);

    /// <summary>"Mon 20 Oct" / "Mo. 20. Okt."</summary>
    public static string Short(DateOnly date) => $"{WeekdaysShort[(int)date.DayOfWeek]} {DayMonth(date)}";

    /// <summary>A moment (UTC) in the zone of the person: "19.10.2026 14:05" / "19/10/2026 14:05".</summary>
    public static string Stamp(DateTime? utc, string? zone)
        => utc is null ? string.Empty : Local(utc.Value, zone).ToString(De ? "dd.MM.yyyy HH:mm" : "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? ParseIso(string? text)
        => DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static TimeOnly? ParseTime(string? text)
        => TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>The time zones a person can choose (the IANA ids of the system).</summary>
    public static IReadOnlyList<string> ZoneIds { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id).Where(id => id.Contains('/', StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
}
