using MatDo.Data;
using MatDo.Services.Parsing;

namespace MatDo.Tests;

public sealed class QuickAddParserTests
{
    // Monday, 19 October 2026, 10:00
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static ParseContext Ctx(bool newProjects = false, TimeOnly? now = null) => new()
    {
        Today = Today,
        Now = now ?? new TimeOnly(10, 0),
        Projects = ["EIMER", "Haus und Garten", "Arbeit", "Eingang"],
        Labels = ["einkauf", "telefon", "wichtig"],
        Sections = new Dictionary<string, IReadOnlyList<string>> { ["arbeit"] = ["Planung", "In Arbeit"], [string.Empty] = [] },
        Keywords = new Dictionary<KeywordKind, IReadOnlyList<string>>
        {
            [KeywordKind.Project] = ["Projekt", "Project"],
            [KeywordKind.Label] = ["Label", "Etikett"],
            [KeywordKind.Priority] = ["Priorität", "Priority"],
            [KeywordKind.Section] = ["Abschnitt"],
            [KeywordKind.Deadline] = ["Deadline", "Frist"],
            [KeywordKind.Description] = ["Beschreibung", "Notiz"],
        },
        AllowNewProjects = newProjects,
    };

    private static ParseResult P(string text, ParseContext? ctx = null) => QuickAddParser.Parse(text, ctx ?? Ctx());

    [Fact]
    public void Plain_text_is_the_title()
    {
        var r = P("Milch kaufen");
        Assert.Equal("Milch kaufen", r.Title);
        Assert.Null(r.DueDate);
        Assert.Null(r.Project);
        Assert.Empty(r.Tokens);
    }

    [Theory]
    [InlineData("Milch kaufen heute", 0)]
    [InlineData("Milch kaufen morgen", 1)]
    [InlineData("Milch kaufen übermorgen", 2)]
    [InlineData("Milch kaufen today", 0)]
    [InlineData("Milch kaufen tomorrow", 1)]
    [InlineData("Morgen Milch kaufen", 1)]
    [InlineData("Milch kaufen in 3 Tagen", 3)]
    [InlineData("Milch kaufen in drei Tagen", 3)]
    [InlineData("Milch kaufen in 2 Wochen", 14)]
    [InlineData("Milch kaufen in einer Woche", 7)]
    [InlineData("Milch kaufen in 5 days", 5)]
    public void Relative_dates(string text, int days)
    {
        var r = P(text);
        Assert.Equal(Today.AddDays(days), r.DueDate);
        Assert.Equal("Milch kaufen", r.Title);
    }

    [Theory]
    [InlineData("Bericht am Freitag", 2026, 10, 23)]
    [InlineData("Bericht Freitag", 2026, 10, 23)]
    [InlineData("Bericht am Montag", 2026, 10, 19)]
    [InlineData("Bericht nächsten Montag", 2026, 10, 26)]
    [InlineData("Bericht next friday", 2026, 10, 23)]
    [InlineData("Bericht am 24.12.", 2026, 12, 24)]
    [InlineData("Bericht am 24.12.2027", 2027, 12, 24)]
    [InlineData("Bericht am 1.3.", 2027, 3, 1)]
    [InlineData("Bericht 2026-11-05", 2026, 11, 5)]
    [InlineData("Bericht am 24. Dezember", 2026, 12, 24)]
    [InlineData("Bericht 3. März", 2027, 3, 3)]
    [InlineData("Bericht on Dec 24", 2026, 12, 24)]
    [InlineData("Bericht December 24th", 2026, 12, 24)]
    [InlineData("Bericht nächste Woche", 2026, 10, 26)]
    [InlineData("Bericht am Wochenende", 2026, 10, 24)]
    [InlineData("Bericht Monatsende", 2026, 10, 31)]
    [InlineData("Bericht bis Freitag", 2026, 10, 23)]
    [InlineData("Bericht am 25.", 2026, 10, 25)]
    public void Absolute_dates(string text, int y, int m, int d)
    {
        var r = P(text);
        Assert.Equal(new DateOnly(y, m, d), r.DueDate);
        Assert.Equal("Bericht", r.Title);
    }

    [Theory]
    [InlineData("Anrufen morgen um 15 Uhr", 15, 0)]
    [InlineData("Anrufen morgen 15:30", 15, 30)]
    [InlineData("Anrufen morgen um 9.30 Uhr", 9, 30)]
    [InlineData("Anrufen morgen at 3pm", 15, 0)]
    [InlineData("Anrufen morgen 3:15 pm", 15, 15)]
    [InlineData("Anrufen morgen um 7 abends", 19, 0)]
    [InlineData("Anrufen morgen um halb 4", 3, 30)]
    [InlineData("Anrufen morgen um halb 5 nachmittags", 16, 30)]
    [InlineData("Anrufen morgen um drei Uhr nachmittags", 15, 0)]
    [InlineData("Anrufen morgen früh", 8, 0)]
    [InlineData("Anrufen morgen Abend", 19, 0)]
    public void Times(string text, int h, int min)
    {
        var r = P(text);
        Assert.Equal(Today.AddDays(1), r.DueDate);
        Assert.Equal(new TimeOnly(h, min), r.DueTime);
        Assert.Equal("Anrufen", r.Title);
    }

    [Fact]
    public void A_time_alone_is_today_or_tomorrow_when_it_has_passed()
    {
        Assert.Equal(Today, P("Anrufen um 15 Uhr").DueDate);
        Assert.Equal(Today.AddDays(1), P("Anrufen um 8 Uhr").DueDate);
    }

    [Fact]
    public void A_bare_number_is_no_time()
    {
        var r = P("3 Äpfel kaufen");
        Assert.Null(r.DueTime);
        Assert.Equal("3 Äpfel kaufen", r.Title);
    }

    [Fact]
    public void Project_by_hash_and_by_keyword()
    {
        Assert.Equal("EIMER", P("Angebot schreiben #eimer").Project);
        Assert.Equal("Haus und Garten", P("Rasen mähen #Haus und Garten morgen").Project);

        var r = P("Projekt EIMER Angebot schreiben übermorgen 9:30");
        Assert.Equal("EIMER", r.Project);
        Assert.Equal("Angebot schreiben", r.Title);
        Assert.Equal(Today.AddDays(2), r.DueDate);
        Assert.Equal(new TimeOnly(9, 30), r.DueTime);
    }

    [Fact]
    public void An_unknown_project_stays_in_the_title_unless_new_ones_are_allowed()
    {
        var r = P("Projekt Neubau planen");
        Assert.Null(r.Project);
        Assert.Equal("Projekt Neubau planen", r.Title);

        var n = P("Projekt Neubau planen", Ctx(newProjects: true));
        Assert.Equal("Neubau", n.Project);
        Assert.True(n.ProjectIsNew);
        Assert.Equal("planen", n.Title);
    }

    [Fact]
    public void Labels_priority_and_section()
    {
        var r = P("Mama anrufen @telefon @neu p1 #Arbeit /Planung");
        Assert.Equal(["telefon", "neu"], r.Labels);
        Assert.Equal(1, r.Priority);
        Assert.Equal("Arbeit", r.Project);
        Assert.Equal("Planung", r.Section);
        Assert.Equal("Mama anrufen", r.Title);

        var k = P("Mama anrufen Label wichtig Priorität 2 Projekt Arbeit Abschnitt In Arbeit");
        Assert.Equal(["wichtig"], k.Labels);
        Assert.Equal(2, k.Priority);
        Assert.Equal("In Arbeit", k.Section);
        Assert.Equal("Mama anrufen", k.Title);

        Assert.Equal(1, P("Steuer Priorität hoch").Priority);
    }

    [Fact]
    public void An_address_is_no_label()
    {
        var r = P("Mail an max@example.com schreiben");
        Assert.Empty(r.Labels);
        Assert.Equal("Mail an max@example.com schreiben", r.Title);
    }

    [Theory]
    [InlineData("Müll rausbringen jeden Montag", "w:1:1", 2026, 10, 19)]
    [InlineData("Müll rausbringen jeden Dienstag", "w:1:2", 2026, 10, 20)]
    [InlineData("Müll rausbringen täglich", "d:1", 2026, 10, 19)]
    [InlineData("Müll rausbringen jeden Tag", "d:1", 2026, 10, 19)]
    [InlineData("Müll rausbringen every day", "d:1", 2026, 10, 19)]
    [InlineData("Müll rausbringen alle 2 Wochen", "w:2", 2026, 10, 19)]
    [InlineData("Müll rausbringen werktags", "wd", 2026, 10, 19)]
    [InlineData("Müll rausbringen jeden Monat", "m:1", 2026, 10, 19)]
    [InlineData("Müll rausbringen jeden 15.", "m:1:15", 2026, 11, 15)]
    public void Recurrence(string text, string rule, int y, int m, int d)
    {
        var r = P(text);
        Assert.Equal(rule, r.Recurrence);
        Assert.Equal(new DateOnly(y, m, d), r.DueDate);
        Assert.Equal("Müll rausbringen", r.Title);
    }

    [Fact]
    public void Recurrence_with_a_time_that_has_passed_starts_tomorrow()
    {
        var r = P("Tabletten jeden Tag um 8 Uhr");
        Assert.Equal("d:1", r.Recurrence);
        Assert.Equal(new TimeOnly(8, 0), r.DueTime);
        Assert.Equal(Today.AddDays(1), r.DueDate);
    }

    [Fact]
    public void Deadline_and_description()
    {
        var r = P("Steuererklärung morgen Deadline 31.12. Beschreibung Belege von 2025 sammeln");
        Assert.Equal(Today.AddDays(1), r.DueDate);
        Assert.Equal(new DateOnly(2026, 12, 31), r.Deadline);
        Assert.Equal("Belege von 2025 sammeln", r.Description);
        Assert.Equal("Steuererklärung", r.Title);

        Assert.Equal(new DateOnly(2026, 10, 23), P("Antrag {Freitag}").Deadline);
    }

    [Fact]
    public void Dictation_with_punctuation()
    {
        var r = P("Morgen um 17 Uhr Milch kaufen. Projekt EIMER.");
        Assert.Equal("Milch kaufen", r.Title);
        Assert.Equal("EIMER", r.Project);
        Assert.Equal(Today.AddDays(1), r.DueDate);
        Assert.Equal(new TimeOnly(17, 0), r.DueTime);
    }

    [Fact]
    public void Dictation_with_several_sentences()
    {
        var r = P("Morgen um 17 Uhr Milch kaufen. Projekt EIMER. Priorität 2.");
        Assert.Equal("Milch kaufen", r.Title);
        Assert.Equal(2, r.Priority);
        Assert.Equal("EIMER", r.Project);
    }

    [Fact]
    public void Only_the_first_date_counts()
    {
        var r = P("Termin am Montag verschieben auf Freitag");
        Assert.Equal(Today, r.DueDate);
        Assert.Equal("Termin verschieben auf Freitag", r.Title);
    }

    [Fact]
    public void A_kind_that_is_switched_off_is_not_read()
    {
        var ctx = new ParseContext
        {
            Today = Today,
            Now = new TimeOnly(10, 0),
            Disabled = new HashSet<TokenKind> { TokenKind.Date, TokenKind.Time },
        };
        var r = QuickAddParser.Parse("Buch Morgen lesen", ctx);
        Assert.Null(r.DueDate);
        Assert.Equal("Buch Morgen lesen", r.Title);
    }

    [Fact]
    public void Tokens_point_at_the_text()
    {
        const string text = "Milch kaufen morgen #EIMER p2";
        var r = P(text);
        Assert.Contains(r.Tokens, t => t.Kind == TokenKind.Date && text.Substring(t.Start, t.Length) == "morgen");
        Assert.Contains(r.Tokens, t => t.Kind == TokenKind.Project && text.Substring(t.Start, t.Length) == "#EIMER");
        Assert.Contains(r.Tokens, t => t.Kind == TokenKind.Priority && text.Substring(t.Start, t.Length) == "p2");
    }

    [Fact]
    public void Abbreviation_keeps_its_full_stop()
    {
        Assert.Equal("Termin bei Dr.", P("Termin bei Dr.").Title);
        Assert.Equal("Rechnung schreiben", P("Rechnung schreiben.").Title);
    }
}
