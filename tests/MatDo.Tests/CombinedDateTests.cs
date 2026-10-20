using MatDo.Data;
using MatDo.Services.Parsing;

namespace MatDo.Tests;

/// <summary>Dates and times said the way people dictate them: a weekday of a named week, the week after next, a moment from now, and "remind me".</summary>
public sealed class CombinedDateTests
{
    // Monday, 19 October 2026, 10:00
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static ParseResult P(string text, TimeOnly? now = null) => QuickAddParser.Parse(text, new ParseContext
    {
        Today = Today,
        Now = now ?? new TimeOnly(10, 0),
        Projects = ["EIMER"],
        Keywords = new Dictionary<KeywordKind, IReadOnlyList<string>> { [KeywordKind.Project] = ["Projekt"], [KeywordKind.Priority] = ["Priorität"] },
    });

    [Theory]
    [InlineData("Nächste Woche Mittwoch um 11 Uhr xyz", "2026-10-28", "11:00", "xyz")]
    [InlineData("Mittwoch nächste Woche 9 Uhr Meeting", "2026-10-28", "09:00", "Meeting")]
    [InlineData("Meeting am Mittwoch nächster Woche", "2026-10-28", null, "Meeting")]
    [InlineData("Meeting am Mittwoch der nächsten Woche", "2026-10-28", null, "Meeting")]
    [InlineData("kommende Woche Freitag Bericht", "2026-10-30", null, "Bericht")]
    [InlineData("nächste Woche am Montag Zahnarzt", "2026-10-26", null, "Zahnarzt")]
    [InlineData("next week friday at 3pm report", "2026-10-30", "15:00", "report")]
    [InlineData("report friday next week", "2026-10-30", null, "report")]
    [InlineData("übernächste Woche Mittwoch Termin", "2026-11-04", null, "Termin")]
    [InlineData("Termin week after next tuesday", "2026-11-03", null, "Termin")]
    public void A_weekday_of_a_named_week_is_that_day_in_that_week(string text, string date, string? time, string title)
    {
        var r = P(text);
        Assert.Equal(DateOnly.Parse(date), r.DueDate);
        Assert.Equal(time is null ? null : TimeOnly.Parse(time), r.DueTime);
        Assert.Equal(title, r.Title);
    }

    [Theory]
    [InlineData("übernächsten Freitag 14:30 Zahnarzt", "2026-10-30", "Zahnarzt")]
    [InlineData("übernächste Woche Urlaub planen", "2026-11-02", "Urlaub planen")]
    [InlineData("Urlaub planen week after next", "2026-11-02", "Urlaub planen")]
    public void The_week_after_next(string text, string date, string title)
    {
        var r = P(text);
        Assert.Equal(DateOnly.Parse(date), r.DueDate);
        Assert.Equal(title, r.Title);
    }

    [Theory]
    [InlineData("in 10min Waffel", "10:10")]
    [InlineData("in 10 min Waffel", "10:10")]
    [InlineData("in 10 Minuten Waffel", "10:10")]
    [InlineData("in 2 Stunden Waffel", "12:00")]
    [InlineData("in 1h Waffel", "11:00")]
    [InlineData("in einer Stunde Waffel", "11:00")]
    [InlineData("in einer halben Stunde Waffel", "10:30")]
    [InlineData("Waffel in 15 minutes", "10:15")]
    [InlineData("Waffel in an hour", "11:00")]
    [InlineData("Waffel in half an hour", "10:30")]
    public void A_moment_from_now_is_today_at_that_time(string text, string time)
    {
        var r = P(text);
        Assert.Equal(Today, r.DueDate);
        Assert.Equal(TimeOnly.Parse(time), r.DueTime);
        Assert.Equal("Waffel", r.Title);
    }

    [Fact]
    public void A_moment_from_now_goes_over_midnight()
    {
        var r = P("in 30 Minuten Licht aus", new TimeOnly(23, 50));
        Assert.Equal(Today.AddDays(1), r.DueDate);
        Assert.Equal(new TimeOnly(0, 20), r.DueTime);
    }

    [Theory]
    [InlineData("Erinnerung in 10min an Waffel", "Waffel", "10:10")]
    [InlineData("Erinnere mich in 2 Stunden daran, den Ofen auszumachen", "den Ofen auszumachen", "12:00")]
    [InlineData("Erinnere mich morgen um 9 dass ich Mama anrufe", "ich Mama anrufe", "09:00")]
    [InlineData("Remind me in 15 minutes to call Bob", "call Bob", "10:15")]
    [InlineData("Reminder: tomorrow 8am take out the trash", "take out the trash", "08:00")]
    public void The_words_of_a_reminder_are_not_part_of_the_title(string text, string title, string time)
    {
        var r = P(text);
        Assert.Equal(title, r.Title);
        Assert.Equal(TimeOnly.Parse(time), r.DueTime);
    }

    [Theory]
    [InlineData("Erinnerung")]
    [InlineData("Erinnerung an")]
    public void A_reminder_word_alone_stays_the_title(string text) => Assert.False(string.IsNullOrWhiteSpace(P(text).Title));

    [Fact]
    public void A_combination_of_week_day_time_project_and_priority_is_read_at_once()
    {
        var r = P("Projekt EIMER Angebot schreiben nächste Woche Mittwoch um 11 Uhr Priorität 1");
        Assert.Equal("EIMER", r.Project);
        Assert.Equal(new DateOnly(2026, 10, 28), r.DueDate);
        Assert.Equal(new TimeOnly(11, 0), r.DueTime);
        Assert.Equal(1, r.Priority);
        Assert.Equal("Angebot schreiben", r.Title);
    }
}
