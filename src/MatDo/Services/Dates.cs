using System.Globalization;

namespace MatDo.Services;

/// <summary>Dates as a person reads them (German), and "today" in the time zone of a person.</summary>
public static class Dates
{
    private static readonly string[] Weekdays = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
    private static readonly string[] WeekdaysShort = ["So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa."];
    private static readonly string[] Months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
    private static readonly string[] MonthsShort = ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sep.", "Okt.", "Nov.", "Dez."];

    /// <summary>1 = Montag .. 7 = Sonntag.</summary>
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

    /// <summary>"Heute", "Morgen", "Gestern", the weekday within the next six days, else "24. Okt." (with the year when it is another one).</summary>
    public static string Relative(DateOnly date, DateOnly today)
    {
        var diff = date.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => "Heute",
            1 => "Morgen",
            2 => "Übermorgen",
            -1 => "Gestern",
            > 2 and < 7 => Weekdays[(int)date.DayOfWeek],
            _ => date.Year == today.Year ? $"{date.Day}. {MonthsShort[date.Month - 1]}" : $"{date.Day}. {MonthsShort[date.Month - 1]} {date.Year}",
        };
    }

    public static string Relative(DateOnly date, TimeOnly? time, DateOnly today)
        => time is null ? Relative(date, today) : $"{Relative(date, today)} {Time(time.Value)}";

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"Montag, 20. Oktober" (the head of a day in the upcoming view).</summary>
    public static string Long(DateOnly date, bool withYear = false)
        => $"{Weekdays[(int)date.DayOfWeek]}, {date.Day}. {Months[date.Month - 1]}" + (withYear ? $" {date.Year}" : string.Empty);

    /// <summary>"Mo. 20. Okt."</summary>
    public static string Short(DateOnly date) => $"{WeekdaysShort[(int)date.DayOfWeek]} {date.Day}. {MonthsShort[date.Month - 1]}";

    /// <summary>A moment (UTC) in the zone of the person: "19.10.2026 14:05".</summary>
    public static string Stamp(DateTime? utc, string? zone)
        => utc is null ? string.Empty : Local(utc.Value, zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? ParseIso(string? text)
        => DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static TimeOnly? ParseTime(string? text)
        => TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>The time zones a person can choose (the IANA ids of the system).</summary>
    public static IReadOnlyList<string> ZoneIds { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.Id).Where(id => id.Contains('/', StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
}
