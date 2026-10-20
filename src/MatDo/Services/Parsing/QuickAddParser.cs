using System.Globalization;
using System.Text.RegularExpressions;
using MatDo.Data;

namespace MatDo.Services.Parsing;

/// <summary>What the parser finds in a text: the kind and where it stands (for the highlight in the editor).</summary>
public enum TokenKind
{
    Date,
    Time,
    Recurrence,
    Project,
    Section,
    Label,
    Priority,
    Deadline,
    Description,
}

public sealed record ParsedToken(TokenKind Kind, int Start, int Length, string Text);

/// <summary>What the parser knows about the person: today in their zone, their projects, labels, sections and keywords.</summary>
public sealed class ParseContext
{
    public required DateOnly Today { get; init; }

    public required TimeOnly Now { get; init; }

    public IReadOnlyList<string> Projects { get; init; } = [];

    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>The sections per project name (lower case); the key "" holds those of the inbox.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Sections { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyDictionary<KeywordKind, IReadOnlyList<string>> Keywords { get; init; } = new Dictionary<KeywordKind, IReadOnlyList<string>>();

    /// <summary>A project that does not exist is taken as a new one (otherwise the words stay in the title).</summary>
    public bool AllowNewProjects { get; init; }

    /// <summary>A label that does not exist is taken as a new one.</summary>
    public bool AllowNewLabels { get; init; } = true;

    /// <summary>The kinds the person switched off in the editor (a date that is really part of the title).</summary>
    public IReadOnlySet<TokenKind> Disabled { get; init; } = new HashSet<TokenKind>();
}

public sealed class ParseResult
{
    public string Title { get; set; } = string.Empty;

    public string? Project { get; set; }

    public bool ProjectIsNew { get; set; }

    public string? Section { get; set; }

    public List<string> Labels { get; } = [];

    public int? Priority { get; set; }

    public DateOnly? DueDate { get; set; }

    public TimeOnly? DueTime { get; set; }

    public string? Recurrence { get; set; }

    public string? RecurrenceText { get; set; }

    public DateOnly? Deadline { get; set; }

    public string? Description { get; set; }

    public List<ParsedToken> Tokens { get; } = [];
}

/// <summary>
/// The quick add: reads a task from one line of text, like Todoist does, in German and English.
/// <code>Milch kaufen morgen um 17 Uhr #Einkauf @laden p2</code>
/// <code>Projekt EIMER Angebot schreiben übermorgen 9:30 Priorität 1</code> (the keywords come from the settings of the person)
/// Every finding claims a part of the text; the parts are found from left to right, a longer one wins at the same place, and of each kind only the first counts
/// (labels: all). What is not claimed is the title. The parser is pure: everything it needs is in the <see cref="ParseContext"/>.
/// </summary>
public static partial class QuickAddParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // A word starts and ends where no letter or digit touches it.
    private const string B = @"(?<![\p{L}\p{N}_])";
    private const string E = @"(?![\p{L}\p{N}_])";

    private const string Number = "(?<n>\\d{1,3}|eine[nmr]?|ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwoelf|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)";

    private const string Weekdays = "montag|dienstag|mittwoch|donnerstag|freitag|samstag|sonnabend|sonntag|monday|tuesday|wednesday|thursday|friday|saturday|sunday";

    private const string Months = "januar|jänner|jan|februar|feb|märz|maerz|mär|mrz|april|apr|mai|may|juni|june|jun|juli|july|jul|august|aug|september|sept|sep|oktober|october|okt|oct|november|nov|dezember|december|dez|dec|january|february|march|mar";

    private const string DayParts = "früh|frueh|morgens|vormittags?|mittags?|nachmittags?|abends?|nachts?|morning|noon|afternoon|evening|night|tonight";

    private enum Slot
    {
        Date,
        Time,
        Project,
        Section,
        Priority,
        Deadline,
        Description,
    }

    private sealed class Candidate
    {
        public required TokenKind Kind { get; init; }

        public required int Start { get; init; }

        public required int Length { get; init; }

        public HashSet<Slot> Slots { get; } = [];

        public DateOnly? Date { get; init; }

        public TimeOnly? Time { get; init; }

        public string? Rule { get; init; }

        public string? Name { get; init; }

        public bool IsNew { get; init; }

        public int? Priority { get; init; }

        public int End => Start + Length;
    }

    public static ParseResult Parse(string? text, ParseContext ctx)
    {
        var result = new ParseResult();
        text ??= string.Empty;
        if (text.Length > 2000)
        {
            text = text[..2000];
        }

        var candidates = new List<Candidate>();
        Description(text, ctx, candidates);
        Projects(text, ctx, candidates);
        Labels(text, ctx, candidates);
        Priorities(text, ctx, candidates);
        Recurrences(text, ctx, candidates);
        Dates(text, ctx, candidates);
        Times(text, ctx, candidates);
        Deadlines(text, ctx, candidates);

        // left to right, the longer one first at the same place; a kind that is already taken does not take a second one
        var accepted = new List<Candidate>();
        var taken = new HashSet<Slot>();
        foreach (var c in candidates.Where(c => !ctx.Disabled.Contains(c.Kind)).OrderBy(c => c.Start).ThenByDescending(c => c.Length))
        {
            if (accepted.Any(a => c.Start < a.End && a.Start < c.End) || c.Slots.Any(taken.Contains))
            {
                continue;
            }

            if (c.Kind == TokenKind.Label && accepted.Any(a => a.Kind == TokenKind.Label && string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            accepted.Add(c);
            taken.UnionWith(c.Slots);
        }

        // the section is looked for in the project that was found (or the inbox)
        Sections(text, ctx, accepted.FirstOrDefault(a => a.Kind == TokenKind.Project)?.Name, accepted);

        foreach (var c in accepted.OrderBy(c => c.Start))
        {
            Apply(result, c);
            result.Tokens.Add(new ParsedToken(c.Kind, c.Start, c.Length, text.Substring(c.Start, c.Length)));
        }

        // a time without a day: today, or tomorrow when the time has passed already
        if (result.DueTime is { } time && result.DueDate is null)
        {
            result.DueDate = time > ctx.Now || result.Recurrence is not null ? ctx.Today : ctx.Today.AddDays(1);
        }

        if (result.Recurrence is not null && result.DueDate is { } first && result.DueTime is { } at && first == ctx.Today && at <= ctx.Now)
        {
            result.DueDate = Services.Recurrence.Next(result.Recurrence, first, ctx.Today);
        }

        result.Title = Title(text, accepted);
        return result;
    }

    private static void Apply(ParseResult r, Candidate c)
    {
        switch (c.Kind)
        {
            case TokenKind.Project:
                r.Project = c.Name;
                r.ProjectIsNew = c.IsNew;
                break;
            case TokenKind.Section:
                r.Section = c.Name;
                break;
            case TokenKind.Label:
                r.Labels.Add(c.Name!);
                break;
            case TokenKind.Priority:
                r.Priority = c.Priority;
                break;
            case TokenKind.Deadline:
                r.Deadline = c.Date;
                break;
            case TokenKind.Description:
                r.Description = c.Name;
                break;
            case TokenKind.Recurrence:
                r.Recurrence = c.Rule;
                r.RecurrenceText = c.Name;
                r.DueDate = c.Date;
                r.DueTime ??= c.Time;
                break;
            default:
                if (c.Date is not null)
                {
                    r.DueDate = c.Date;
                }

                if (c.Time is not null)
                {
                    r.DueTime = c.Time;
                }

                break;
        }
    }

    // ---- the title: what is left ---------------------------------------------------------------------------------------

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    // the words that only say "remind me" at the start of a dictation, with the word that joins them to the task
    [GeneratedRegex(@"^(?:erinnerung|erinnere\s+mich|erinner\s+mich|erinnere|reminder|remind\s+me)(?:\s*[:,]\s*|\s+)(?:(?:daran|an|dass|ans|to|about|that|of)(?:\s*,\s*|\s+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReminderLead();

    [GeneratedRegex(@"\s+([,.;:!?])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"([,;:])\s*([,;:.!?])")]
    private static partial Regex DoublePunctuation();

    // full stops that met where parts were taken out ("kaufen. Projekt EIMER. Priorität 2." -> "kaufen...") become one
    [GeneratedRegex(@"\.(?:\s*\.)+")]
    private static partial Regex DoubleStop();

    private static string Title(string text, IReadOnlyList<Candidate> claims)
    {
        var chars = text.ToCharArray();
        foreach (var c in claims)
        {
            for (var i = c.Start; i < c.End; i++)
            {
                chars[i] = '\u0001';
            }
        }

        var title = new string(chars).Replace('\u0001', ' ');
        title = title.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        title = Spaces().Replace(title, " ");
        title = SpaceBeforePunctuation().Replace(title, "$1");
        title = DoublePunctuation().Replace(title, "$2");
        title = DoubleStop().Replace(title, ".");
        title = title.Trim().Trim(',', ';', ':', '-', '–').Trim();
        var reminder = ReminderLead().Match(title);
        if (reminder.Success && reminder.Length < title.Length)
        {
            title = title[reminder.Length..].Trim();
        }

        // the full stop that a dictation puts at the end of the sentence (but not the one of an abbreviation such as "Dr.")
        if (title.EndsWith('.') && !title.EndsWith("..", StringComparison.Ordinal))
        {
            var lastWord = title[..^1].Split(' ').LastOrDefault() ?? string.Empty;
            if (lastWord.Length > 3)
            {
                title = title[..^1].TrimEnd();
            }
        }

        return title;
    }

    // ---- description: "Beschreibung ..." takes the rest of the text ---------------------------------------------------

    private static void Description(string text, ParseContext ctx, List<Candidate> list)
    {
        var words = KeywordPattern(ctx, KeywordKind.Description);
        if (words is null)
        {
            return;
        }

        foreach (Match m in new Regex($"{B}(?:{words}){E}\\s*:?\\s*(?<rest>\\S.*)$", Opt | RegexOptions.Singleline).Matches(text))
        {
            var rest = m.Groups["rest"].Value.Trim();
            var c = new Candidate { Kind = TokenKind.Description, Start = m.Index, Length = m.Length, Name = rest };
            c.Slots.Add(Slot.Description);
            list.Add(c);
        }
    }

    // ---- projects: #Name, or the keyword ("Projekt EIMER") --------------------------------------------------------------

    [GeneratedRegex(@"(?<!\S)#(?<name>[^\s#@,;!?]+)", Opt)]
    private static partial Regex HashProject();

    private static void Projects(string text, ParseContext ctx, List<Candidate> list)
    {
        // #Name: the longest project that starts there (names may have spaces), else the word
        for (var i = text.IndexOf('#'); i >= 0; i = text.IndexOf('#', i + 1))
        {
            if (i > 0 && !char.IsWhiteSpace(text[i - 1]))
            {
                continue;
            }

            var known = LongestName(text, i + 1, ctx.Projects);
            if (known is not null)
            {
                Add(list, TokenKind.Project, i, known.Value.Length + 1, Slot.Project, name: known.Value.Name);
                continue;
            }

            var m = HashProject().Match(text, i);
            if (m.Success && m.Index == i && ctx.AllowNewProjects)
            {
                var name = TrimWord(m.Groups["name"].Value);
                Add(list, TokenKind.Project, i, name.Length + 1, Slot.Project, name: name, isNew: true);
            }
        }

        var words = KeywordPattern(ctx, KeywordKind.Project);
        if (words is null)
        {
            return;
        }

        foreach (Match m in new Regex($"{B}(?:{words})\\s*:?\\s+", Opt).Matches(text))
        {
            var at = m.Index + m.Length;
            var known = LongestName(text, at, ctx.Projects);
            if (known is not null)
            {
                Add(list, TokenKind.Project, m.Index, m.Length + known.Value.Length, Slot.Project, name: known.Value.Name);
            }
            else if (ctx.AllowNewProjects && NextWord(text, at) is { } word)
            {
                Add(list, TokenKind.Project, m.Index, m.Length + word.Length, Slot.Project, name: word, isNew: true);
            }
        }
    }

    // ---- sections: /Name, or the keyword ("Abschnitt Planung") ---------------------------------------------------------

    private static void Sections(string text, ParseContext ctx, string? project, List<Candidate> accepted)
    {
        if (ctx.Disabled.Contains(TokenKind.Section)
            || !ctx.Sections.TryGetValue((project ?? string.Empty).ToLowerInvariant(), out var sections) || sections.Count == 0)
        {
            return;
        }

        var found = new List<Candidate>();
        for (var i = text.IndexOf('/'); i >= 0; i = text.IndexOf('/', i + 1))
        {
            if (i > 0 && !char.IsWhiteSpace(text[i - 1]))
            {
                continue;
            }

            if (LongestName(text, i + 1, sections) is { } known)
            {
                found.Add(Make(TokenKind.Section, i, known.Length + 1, Slot.Section, known.Name));
            }
        }

        var words = KeywordPattern(ctx, KeywordKind.Section);
        if (words is not null)
        {
            foreach (Match m in new Regex($"{B}(?:{words})\\s*:?\\s+", Opt).Matches(text))
            {
                if (LongestName(text, m.Index + m.Length, sections) is { } known)
                {
                    found.Add(Make(TokenKind.Section, m.Index, m.Length + known.Length, Slot.Section, known.Name));
                }
            }
        }

        var first = found.OrderBy(c => c.Start).FirstOrDefault(c => !accepted.Any(a => c.Start < a.End && a.Start < c.End));
        if (first is not null)
        {
            accepted.Add(first);
        }
    }

    // ---- labels: @name, or the keyword ("Label Einkauf") -------------------------------------------------------------

    [GeneratedRegex(@"(?<!\S)@(?<name>[^\s#@,;!?]+)", Opt)]
    private static partial Regex AtLabel();

    private static void Labels(string text, ParseContext ctx, List<Candidate> list)
    {
        for (var i = text.IndexOf('@'); i >= 0; i = text.IndexOf('@', i + 1))
        {
            if (i > 0 && !char.IsWhiteSpace(text[i - 1]))
            {
                continue;
            }

            var known = LongestName(text, i + 1, ctx.Labels);
            if (known is not null)
            {
                Add(list, TokenKind.Label, i, known.Value.Length + 1, null, name: known.Value.Name);
                continue;
            }

            var m = AtLabel().Match(text, i);
            if (m.Success && m.Index == i && ctx.AllowNewLabels)
            {
                var name = TrimWord(m.Groups["name"].Value);
                if (name.Length > 0)
                {
                    Add(list, TokenKind.Label, i, name.Length + 1, null, name: name, isNew: true);
                }
            }
        }

        var words = KeywordPattern(ctx, KeywordKind.Label);
        if (words is null)
        {
            return;
        }

        foreach (Match m in new Regex($"{B}(?:{words})\\s*:?\\s+", Opt).Matches(text))
        {
            var at = m.Index + m.Length;
            var known = LongestName(text, at, ctx.Labels);
            if (known is not null)
            {
                Add(list, TokenKind.Label, m.Index, m.Length + known.Value.Length, null, name: known.Value.Name);
            }
            else if (ctx.AllowNewLabels && NextWord(text, at) is { } word)
            {
                Add(list, TokenKind.Label, m.Index, m.Length + word.Length, null, name: word, isNew: true);
            }
        }
    }

    // ---- priority: p1 .. p4, !!1 .. !!4, the keyword ("Priorität 1", "Priorität hoch") -----------------------------------

    [GeneratedRegex(@"(?<!\S)(?:p|!!)(?<n>[1-4])(?=$|[\s,.;:!?)])", Opt)]
    private static partial Regex PriorityShort();

    private static void Priorities(string text, ParseContext ctx, List<Candidate> list)
    {
        foreach (Match m in PriorityShort().Matches(text))
        {
            Add(list, TokenKind.Priority, m.Index, m.Length, Slot.Priority, priority: m.Groups["n"].Value[0] - '0');
        }

        var words = KeywordPattern(ctx, KeywordKind.Priority);
        if (words is null)
        {
            return;
        }

        var levels = "(?<p>[1-4]|eins|zwei|drei|vier|one|two|three|four|hoch|high|mittel|medium|niedrig|low|dringend|urgent)";
        foreach (Match m in new Regex($"{B}(?:{words})\\s*:?\\s*{levels}{E}", Opt).Matches(text))
        {
            var p = m.Groups["p"].Value.ToLowerInvariant() switch
            {
                "1" or "eins" or "one" or "hoch" or "high" or "dringend" or "urgent" => 1,
                "2" or "zwei" or "two" or "mittel" or "medium" => 2,
                "3" or "drei" or "three" or "niedrig" or "low" => 3,
                _ => 4,
            };
            Add(list, TokenKind.Priority, m.Index, m.Length, Slot.Priority, priority: p);
        }
    }

    // ---- deadline: "Deadline Freitag", "Frist 24.12.", {date} ---------------------------------------------------------

    private static void Deadlines(string text, ParseContext ctx, List<Candidate> list)
    {
        var words = KeywordPattern(ctx, KeywordKind.Deadline);
        if (words is not null)
        {
            foreach (Match m in new Regex($"{B}(?:{words})\\s*:?\\s+", Opt).Matches(text))
            {
                var at = m.Index + m.Length;
                var date = list.Where(c => c.Kind == TokenKind.Date && c.Start == at && c.Date is not null).OrderByDescending(c => c.Length).FirstOrDefault();
                if (date is not null)
                {
                    Add(list, TokenKind.Deadline, m.Index, date.End - m.Index, Slot.Deadline, date: date.Date);
                }
            }
        }

        for (var i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
        {
            var close = text.IndexOf('}', i);
            if (close < 0)
            {
                break;
            }

            var inner = text.Substring(i + 1, close - i - 1);
            var found = new List<Candidate>();
            Dates(inner, ctx, found);
            var date = found.FirstOrDefault(c => c.Start == 0 && c.Length == inner.Trim().Length && c.Date is not null)
                       ?? found.FirstOrDefault(c => c.Date is not null && inner.Trim().Length == c.Length);
            if (date is not null)
            {
                Add(list, TokenKind.Deadline, i, close - i + 1, Slot.Deadline, date: date.Date);
            }
        }
    }

    // ---- recurrence: "jeden Tag", "jeden Montag", "alle 2 Wochen", "every weekday" -------------------------------------

    private static readonly Regex RecurrenceRegex = new(
        $"{B}(?:(?<daily>täglich|taeglich|daily|jeden\\s+tag|every\\s+day)"
        + "|(?<weekdays>werktags|jeden\\s+werktag|every\\s+weekday|every\\s+workday)"
        + "|(?<weekly>wöchentlich|woechentlich|weekly|jede\\s+woche|every\\s+week)"
        + "|(?<monthly>monatlich|monthly|jeden\\s+monat|every\\s+month)"
        + "|(?<yearly>jährlich|jaehrlich|yearly|annually|jedes\\s+jahr|every\\s+year)"
        + $"|(?:jeden|every)\\s+(?<dow>{Weekdays})"
        + "|(?:jeden|every)\\s+(?<mday>\\d{1,2})(?:\\.|st|nd|rd|th)?(?:\\s+(?:des\\s+monats|of\\s+the\\s+month))?"
        + $"|(?:alle|every)\\s+{Number}\\s+(?<unit>tage?n?|wochen?|monate?n?|jahre?n?|days?|weeks?|months?|years?))(?![\\p{{L}}\\p{{N}}_])",
        Opt);

    private static void Recurrences(string text, ParseContext ctx, List<Candidate> list)
    {
        foreach (Match m in RecurrenceRegex.Matches(text))
        {
            string rule;
            DateOnly first;
            var today = ctx.Today;
            if (m.Groups["daily"].Success)
            {
                rule = "d:1";
                first = today;
            }
            else if (m.Groups["weekdays"].Success)
            {
                rule = "wd";
                first = Services.Recurrence.Next("wd", today.AddDays(-1), today.AddDays(-1));
            }
            else if (m.Groups["weekly"].Success)
            {
                rule = "w:1";
                first = today;
            }
            else if (m.Groups["monthly"].Success)
            {
                rule = "m:1";
                first = today;
            }
            else if (m.Groups["yearly"].Success)
            {
                rule = "y:1";
                first = today;
            }
            else if (m.Groups["dow"].Success)
            {
                var dow = Weekday(m.Groups["dow"].Value);
                rule = $"w:1:{Iso(dow)}";
                first = NextWeekday(today, dow, strictlyAfter: false);
            }
            else if (m.Groups["mday"].Success)
            {
                var day = int.Parse(m.Groups["mday"].Value, CultureInfo.InvariantCulture);
                if (day is < 1 or > 31)
                {
                    continue;
                }

                rule = $"m:1:{day}";
                first = Services.Recurrence.Next(rule, today.AddDays(-1), today.AddDays(-1));
            }
            else
            {
                var n = NumberValue(m.Groups["n"].Value);
                if (n is null or < 1 or > 365)
                {
                    continue;
                }

                var unit = m.Groups["unit"].Value.ToLowerInvariant();
                var letter = unit.StartsWith('t') || unit.StartsWith('d') ? 'd' : unit.StartsWith('w') ? 'w' : unit.StartsWith('m') ? 'm' : 'y';
                rule = $"{letter}:{n}";
                first = today;
            }

            var c = new Candidate { Kind = TokenKind.Recurrence, Start = m.Index, Length = m.Length, Rule = rule, Date = first, Name = m.Value };
            c.Slots.Add(Slot.Date);
            list.Add(c);
        }
    }

    // ---- dates ------------------------------------------------------------------------------------------------------

    private static readonly string Lead = "(?:(?:am|on|bis(?:\\s+zum)?|by|für|fuer|for|ab|due)\\s+)?";

    private static readonly Regex IsoDate = new($"{B}{Lead}(?<y>\\d{{4}})-(?<m>\\d{{1,2}})-(?<d>\\d{{1,2}}){E}", Opt);

    // 24.12. / 24.12.2026 / 24.12.26 (the full stop after the month is needed without a year: "15.30" is a time)
    private static readonly Regex NumericDate = new($"{B}{Lead}(?<d>\\d{{1,2}})\\.(?<m>\\d{{1,2}})\\.(?:(?<y>\\d{{4}}|\\d{{2}}){E})?", Opt);

    // 24/12, 24/12/2026 (the day first, as in Germany and the UK)
    private static readonly Regex SlashDate = new($"{B}{Lead}(?<d>\\d{{1,2}})/(?<m>\\d{{1,2}})(?:/(?<y>\\d{{4}}|\\d{{2}}))?{E}", Opt);

    // 24. Dezember (2026) / 24 Dec / 3rd of March
    private static readonly Regex DayMonth = new($"{B}{Lead}(?:(?:den|the)\\s+)?(?<d>\\d{{1,2}})(?:\\.|st|nd|rd|th)?\\s*(?:of\\s+)?(?<mon>{Months})\\.?(?:\\s+(?<y>\\d{{4}}))?{E}", Opt);

    // December 24(th)(, 2026)
    private static readonly Regex MonthDay = new($"{B}{Lead}(?<mon>{Months})\\.?\\s+(?<d>\\d{{1,2}})(?:st|nd|rd|th)?(?:,?\\s+(?<y>\\d{{4}}))?{E}", Opt);

    // am 15. (this month, or the next when the day has passed)
    private static readonly Regex DayOnly = new($"{B}(?:am|on\\s+the)\\s+(?<d>\\d{{1,2}})(?:\\.|st|nd|rd|th){E}?", Opt);

    private static readonly Regex Relative = new(
        $"{B}{Lead}(?:(?<w>übermorgen|uebermorgen|day\\s+after\\s+tomorrow|heute|today|tonight|morgen|tomorrow|tmr)(?:\\s+(?<part>{DayParts}))?){E}",
        Opt);

    private static readonly Regex InSpan = new($"{B}(?:in|innerhalb\\s+von)\\s+{Number}\\s+(?<unit>tage?n?|wochen?|monate?n?|jahre?n?|days?|weeks?|months?|years?){E}", Opt);

    private static readonly Regex WeekdayRegex = new($"{B}{Lead}(?<next>übernächste[nrs]?|uebernaechste[nrs]?|nächste[nrs]?|naechste[nrs]?|kommende[nrs]?|diese[nrs]?|next|this|coming)?\\s*(?<dow>{Weekdays}){E}", Opt);

    // a week that is named: next week, the week after next ("nächste Woche", "nächster Woche", "übernächste Woche", "next week", "week after next")
    private const string WeekWords = "(?<after>übernächste[nrs]?|uebernaechste[nrs]?)\\s+woche|(?:nächste[nrs]?|naechste[nrs]?|kommende[nrs]?)\\s+woche|next\\s+week|(?<after2>week\\s+after\\s+next)";

    // a weekday in that week, in either order: "nächste Woche Mittwoch", "Mittwoch nächste Woche", "am Mittwoch der nächsten Woche", "next week friday", "friday next week"
    private static readonly Regex WeekThenDay = new($"{B}{Lead}(?:{WeekWords})\\s+(?:am\\s+|on\\s+)?(?<dow>{Weekdays}){E}", Opt);

    private static readonly Regex DayThenWeek = new($"{B}{Lead}(?<dow>{Weekdays})\\s+(?:in\\s+der\\s+|der\\s+|in\\s+)?(?:{WeekWords}){E}", Opt);

    // a moment from now: "in 10 min", "in 10min", "in 2 Stunden", "in einer halben Stunde", "in 15 minutes", "in an hour"
    private static readonly Regex InTime = new(
        $"{B}(?:in|innerhalb\\s+von)\\s+(?:(?<half>einer\\s+halben\\s+stunde|half\\s+an\\s+hour|a\\s+half\\s+hour)|{Number}\\s*(?<unit>minuten|minute|minutes|mins?|stunden|stunde|std|hours?|hrs?|h)){E}",
        Opt);

    private static readonly Regex Named = new(
        $"{B}{Lead}(?:(?<nextweek>nächste\\s+woche|naechste\\s+woche|kommende\\s+woche|next\\s+week)"
        + "|(?<nextmonth>nächsten\\s+monat|naechsten\\s+monat|kommenden\\s+monat|next\\s+month)"
        + "|(?<nextyear>nächstes\\s+jahr|naechstes\\s+jahr|next\\s+year)"
        + "|(?<weekafter>übernächste[nrs]?\\s+woche|uebernaechste[nrs]?\\s+woche|week\\s+after\\s+next)"
        + "|(?<weekend>(?:am\\s+|this\\s+|dieses\\s+)?wochenende|(?:this\\s+)?weekend)"
        + "|(?<endweek>ende\\s+der\\s+woche|end\\s+of\\s+(?:the\\s+)?week)"
        + $"|(?<endmonth>monatsende|ende\\s+des\\s+monats|end\\s+of\\s+(?:the\\s+)?month)){E}",
        Opt);

    private static void Dates(string text, ParseContext ctx, List<Candidate> list)
    {
        var today = ctx.Today;

        foreach (Match m in IsoDate.Matches(text))
        {
            AddDate(list, m, MakeDate(Int(m, "y"), Int(m, "m"), Int(m, "d")));
        }

        foreach (Match m in NumericDate.Matches(text))
        {
            AddDate(list, m, Resolve(today, m.Groups["y"].Success ? Year(Int(m, "y")) : null, Int(m, "m"), Int(m, "d")));
        }

        foreach (Match m in SlashDate.Matches(text))
        {
            AddDate(list, m, Resolve(today, m.Groups["y"].Success ? Year(Int(m, "y")) : null, Int(m, "m"), Int(m, "d")));
        }

        foreach (Match m in DayMonth.Matches(text))
        {
            AddDate(list, m, Resolve(today, m.Groups["y"].Success ? Int(m, "y") : null, Month(m.Groups["mon"].Value), Int(m, "d")));
        }

        foreach (Match m in MonthDay.Matches(text))
        {
            AddDate(list, m, Resolve(today, m.Groups["y"].Success ? Int(m, "y") : null, Month(m.Groups["mon"].Value), Int(m, "d")));
        }

        foreach (Match m in DayOnly.Matches(text))
        {
            var d = Int(m, "d");
            var date = MakeDate(today.Year, today.Month, d);
            if (date is null || date < today)
            {
                var next = today.AddMonths(1);
                date = MakeDate(next.Year, next.Month, d);
            }

            AddDate(list, m, date);
        }

        foreach (Match m in Relative.Matches(text))
        {
            var w = m.Groups["w"].Value.ToLowerInvariant();
            var date = w switch
            {
                "heute" or "today" or "tonight" => today,
                "morgen" or "tomorrow" or "tmr" => today.AddDays(1),
                _ => today.AddDays(2),
            };
            TimeOnly? time = w == "tonight" ? new TimeOnly(19, 0) : null;
            if (m.Groups["part"].Success)
            {
                time = DayPart(m.Groups["part"].Value);
            }

            var c = new Candidate { Kind = TokenKind.Date, Start = m.Index, Length = m.Length, Date = date, Time = time };
            c.Slots.Add(Slot.Date);
            if (time is not null)
            {
                c.Slots.Add(Slot.Time);
            }

            list.Add(c);
        }

        foreach (Match m in InSpan.Matches(text))
        {
            var n = NumberValue(m.Groups["n"].Value);
            if (n is null or > 3650)
            {
                continue;
            }

            var unit = m.Groups["unit"].Value.ToLowerInvariant();
            var date = unit[0] switch
            {
                't' or 'd' => today.AddDays(n.Value),
                'w' => today.AddDays(7 * n.Value),
                'm' => today.AddMonths(n.Value),
                _ => today.AddYears(n.Value),
            };
            AddDate(list, m, date);
        }

        foreach (Match m in WeekdayRegex.Matches(text))
        {
            var dow = Weekday(m.Groups["dow"].Value);
            var word = m.Groups["next"].Value.ToLowerInvariant();
            var strictly = word.Length > 0 && !word.StartsWith("dies", StringComparison.Ordinal) && word != "this";
            var date = NextWeekday(today, dow, strictly);
            // "übernächsten Freitag": the one after the next one
            AddDate(list, m, word.StartsWith("über", StringComparison.Ordinal) || word.StartsWith("ueber", StringComparison.Ordinal) ? date.AddDays(7) : date);
        }

        // a weekday of a named week (the weeks start on Monday): longer than the week or the weekday alone, so it wins over both
        foreach (var regex in new[] { WeekThenDay, DayThenWeek })
        {
            foreach (Match m in regex.Matches(text))
            {
                var weeks = m.Groups["after"].Success || m.Groups["after2"].Success ? 2 : 1;
                var dow = Weekday(m.Groups["dow"].Value);
                AddDate(list, m, MondayOf(today).AddDays(7 * weeks + ((int)dow + 6) % 7));
            }
        }

        // a moment from now: a day and a time (over midnight too)
        foreach (Match m in InTime.Matches(text))
        {
            int minutes;
            if (m.Groups["half"].Success)
            {
                minutes = 30;
            }
            else
            {
                var n = NumberValue(m.Groups["n"].Value);
                var unit = m.Groups["unit"].Value.ToLowerInvariant();
                var hours = unit.StartsWith('s') || unit.StartsWith('h');
                if (n is null or 0 || (hours ? n > 72 : n > 600))
                {
                    continue;
                }

                minutes = hours ? n.Value * 60 : n.Value;
            }

            var at = today.ToDateTime(ctx.Now).AddMinutes(minutes);
            var c = new Candidate { Kind = TokenKind.Date, Start = m.Index, Length = m.Length, Date = DateOnly.FromDateTime(at), Time = new TimeOnly(at.Hour, at.Minute) };
            c.Slots.Add(Slot.Date);
            c.Slots.Add(Slot.Time);
            list.Add(c);
        }

        foreach (Match m in Named.Matches(text))
        {
            DateOnly date;
            if (m.Groups["nextweek"].Success)
            {
                date = NextWeekday(today, DayOfWeek.Monday, strictlyAfter: true);
            }
            else if (m.Groups["nextmonth"].Success)
            {
                date = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            }
            else if (m.Groups["nextyear"].Success)
            {
                date = new DateOnly(today.Year + 1, 1, 1);
            }
            else if (m.Groups["weekafter"].Success)
            {
                date = MondayOf(today).AddDays(14);
            }
            else if (m.Groups["weekend"].Success)
            {
                date = today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? today : NextWeekday(today, DayOfWeek.Saturday, strictlyAfter: false);
            }
            else if (m.Groups["endweek"].Success)
            {
                date = today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? today : NextWeekday(today, DayOfWeek.Friday, strictlyAfter: false);
            }
            else
            {
                date = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            }

            AddDate(list, m, date);
        }
    }

    // ---- times ------------------------------------------------------------------------------------------------------

    private static readonly string TimeLead = "(?:(?:um|gegen|ab|at|@|by)\\s*)?";

    private static readonly string Suffix = "(?:\\s*(?<ampm>am|pm|a\\.m\\.|p\\.m\\.))?(?:\\s*(?<uhr>uhr|h|o'clock|oclock)(?![\\p{L}\\p{N}_]))?(?:\\s+(?<part>morgens|vormittags|nachmittags|abends|nachts|in\\s+the\\s+(?:morning|afternoon|evening)|am\\s+(?:morgen|vormittag|nachmittag|abend)))?";

    // 15:30, 9.30 Uhr, um 15 Uhr, 3pm, 3:30 pm, um 7 abends
    private static readonly Regex ClockTime = new($"{B}{TimeLead}(?<h>\\d{{1,2}})(?:[:.](?<min>\\d{{2}}))?{Suffix}{E}", Opt);

    // um drei (Uhr), at five pm
    private static readonly Regex WordTime = new($"{B}(?:um|at)\\s+(?<w>eins|ein|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwoelf|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve){Suffix}{E}", Opt);

    // halb vier (= 3:30)
    private static readonly Regex HalfTime = new($"{B}(?:um\\s+)?halb\\s+(?<h>\\d{{1,2}}|eins|zwei|drei|vier|fünf|fuenf|sechs|sieben|acht|neun|zehn|elf|zwölf|zwoelf){Suffix}{E}", Opt);

    private static void Times(string text, ParseContext ctx, List<Candidate> list)
    {
        foreach (Match m in ClockTime.Matches(text))
        {
            var lead = m.Value.TrimStart();
            var hasLead = lead.StartsWith("um", StringComparison.OrdinalIgnoreCase) || lead.StartsWith("gegen", StringComparison.OrdinalIgnoreCase)
                          || lead.StartsWith("at", StringComparison.OrdinalIgnoreCase) || lead.StartsWith('@') || lead.StartsWith("ab", StringComparison.OrdinalIgnoreCase)
                          || lead.StartsWith("by", StringComparison.OrdinalIgnoreCase);
            var hasMinutes = m.Groups["min"].Success;
            var colon = hasMinutes && text[m.Groups["min"].Index - 1] == ':';

            // a bare number is not a time ("3 Äpfel"); "15.30" only with "um" or "Uhr" (it could be a date or an amount)
            if (!(colon || hasLead || m.Groups["uhr"].Success || m.Groups["ampm"].Success))
            {
                continue;
            }

            var time = MakeTime(Int(m, "h"), hasMinutes ? Int(m, "min") : 0, m.Groups["ampm"].Value, m.Groups["part"].Value);
            if (time is not null)
            {
                Add(list, TokenKind.Time, m.Index, m.Length, Slot.Time, time: time);
            }
        }

        foreach (Match m in WordTime.Matches(text))
        {
            var time = MakeTime(NumberValue(m.Groups["w"].Value) ?? -1, 0, m.Groups["ampm"].Value, m.Groups["part"].Value);
            if (time is not null)
            {
                Add(list, TokenKind.Time, m.Index, m.Length, Slot.Time, time: time);
            }
        }

        foreach (Match m in HalfTime.Matches(text))
        {
            var h = NumberValue(m.Groups["h"].Value) ?? -1;
            var time = MakeTime(h == 0 ? 23 : h - 1, 30, m.Groups["ampm"].Value, m.Groups["part"].Value);
            if (time is not null)
            {
                Add(list, TokenKind.Time, m.Index, m.Length, Slot.Time, time: time);
            }
        }
    }

    private static TimeOnly? MakeTime(int hour, int minute, string ampm, string part)
    {
        if (hour < 0 || hour > 24 || minute is < 0 or > 59)
        {
            return null;
        }

        var a = ampm.Replace(".", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var p = part.ToLowerInvariant();
        if (a == "pm" || p.Contains("nachmittag", StringComparison.Ordinal) || p.Contains("abend", StringComparison.Ordinal) || p.Contains("afternoon", StringComparison.Ordinal) || p.Contains("evening", StringComparison.Ordinal))
        {
            if (hour < 12)
            {
                hour += 12;
            }
        }
        else if (a == "am" && hour == 12)
        {
            hour = 0;
        }
        else if (p.Contains("nacht", StringComparison.Ordinal) && hour is >= 8 and < 12)
        {
            hour += 12;
        }

        if (hour == 24)
        {
            hour = 0;
        }

        return hour > 23 ? null : new TimeOnly(hour, minute);
    }

    private static TimeOnly DayPart(string part) => part.ToLowerInvariant() switch
    {
        "früh" or "frueh" or "morgens" or "morning" => new TimeOnly(8, 0),
        var p when p.StartsWith("vormittag", StringComparison.Ordinal) => new TimeOnly(10, 0),
        var p when p.StartsWith("mittag", StringComparison.Ordinal) || p == "noon" => new TimeOnly(12, 0),
        var p when p.StartsWith("nachmittag", StringComparison.Ordinal) || p == "afternoon" => new TimeOnly(15, 0),
        var p when p.StartsWith("nacht", StringComparison.Ordinal) || p == "night" => new TimeOnly(21, 0),
        _ => new TimeOnly(19, 0),
    };

    // ---- helpers ----------------------------------------------------------------------------------------------------

    /// <summary>The keywords of a kind as an alternation for a regex, the longest first; null when the person has none.</summary>
    private static string? KeywordPattern(ParseContext ctx, KeywordKind kind)
    {
        if (!ctx.Keywords.TryGetValue(kind, out var words) || words.Count == 0)
        {
            return null;
        }

        var parts = words.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => Regex.Escape(w.Trim()).Replace("\\ ", "\\s+", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(w => w.Length).ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    /// <summary>The longest of <paramref name="names"/> that stands at <paramref name="at"/> and ends at the end of a word (any case).</summary>
    private static (string Name, int Length)? LongestName(string text, int at, IEnumerable<string> names)
    {
        (string, int)? best = null;
        foreach (var name in names)
        {
            if (name.Length == 0 || at + name.Length > text.Length || (best is not null && name.Length <= best.Value.Item2))
            {
                continue;
            }

            if (string.Compare(text, at, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            var end = at + name.Length;
            if (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            {
                continue;
            }

            best = (name, name.Length);
        }

        return best;
    }

    [GeneratedRegex(@"^[\p{L}\p{N}_\-\.&+]+")]
    private static partial Regex WordRegex();

    private static string? NextWord(string text, int at)
    {
        var m = WordRegex().Match(text[at..]);
        if (!m.Success)
        {
            return null;
        }

        var word = TrimWord(m.Value);
        return word.Length == 0 ? null : word;
    }

    private static string TrimWord(string word) => word.TrimEnd('.', ',', ';', ':', '!', '?', ')', '-');

    private static void Add(List<Candidate> list, TokenKind kind, int start, int length, Slot? slot, string? name = null, bool isNew = false, int? priority = null, DateOnly? date = null, TimeOnly? time = null)
    {
        var c = new Candidate { Kind = kind, Start = start, Length = length, Name = name, IsNew = isNew, Priority = priority, Date = date, Time = time };
        if (slot is not null)
        {
            c.Slots.Add(slot.Value);
        }

        list.Add(c);
    }

    private static Candidate Make(TokenKind kind, int start, int length, Slot slot, string name)
    {
        var c = new Candidate { Kind = kind, Start = start, Length = length, Name = name };
        c.Slots.Add(slot);
        return c;
    }

    private static void AddDate(List<Candidate> list, Match m, DateOnly? date)
    {
        if (date is null)
        {
            return;
        }

        var c = new Candidate { Kind = TokenKind.Date, Start = m.Index, Length = m.Length, Date = date };
        c.Slots.Add(Slot.Date);
        list.Add(c);
    }

    private static int Int(Match m, string group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int Year(int y) => y < 100 ? 2000 + y : y;

    private static DateOnly? MakeDate(int y, int m, int d)
        => m is >= 1 and <= 12 && y is >= 1900 and <= 2200 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;

    /// <summary>A day and month without a year: this year, or the next when it has passed.</summary>
    private static DateOnly? Resolve(DateOnly today, int? year, int month, int day)
    {
        if (year is not null)
        {
            return MakeDate(year.Value, month, day);
        }

        var date = MakeDate(today.Year, month, day);
        if (date is not null && date < today)
        {
            date = MakeDate(today.Year + 1, month, day);
        }

        return date;
    }

    internal static DateOnly NextWeekday(DateOnly from, DayOfWeek dow, bool strictlyAfter)
    {
        var diff = ((int)dow - (int)from.DayOfWeek + 7) % 7;
        if (diff == 0 && strictlyAfter)
        {
            diff = 7;
        }

        return from.AddDays(diff);
    }

    internal static int Iso(DayOfWeek dow) => dow == DayOfWeek.Sunday ? 7 : (int)dow;

    /// <summary>The Monday of the week a day is in (a week starts on Monday, ISO 8601).</summary>
    private static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private static DayOfWeek Weekday(string word) => word.ToLowerInvariant() switch
    {
        "montag" or "monday" => DayOfWeek.Monday,
        "dienstag" or "tuesday" => DayOfWeek.Tuesday,
        "mittwoch" or "wednesday" => DayOfWeek.Wednesday,
        "donnerstag" or "thursday" => DayOfWeek.Thursday,
        "freitag" or "friday" => DayOfWeek.Friday,
        "samstag" or "sonnabend" or "saturday" => DayOfWeek.Saturday,
        _ => DayOfWeek.Sunday,
    };

    private static int Month(string word)
    {
        var w = word.ToLowerInvariant();
        return w switch
        {
            _ when w.StartsWith("jan", StringComparison.Ordinal) || w.StartsWith("jän", StringComparison.Ordinal) => 1,
            _ when w.StartsWith("feb", StringComparison.Ordinal) => 2,
            _ when w.StartsWith("mär", StringComparison.Ordinal) || w.StartsWith("mae", StringComparison.Ordinal) || w == "mrz" || w.StartsWith("mar", StringComparison.Ordinal) => 3,
            _ when w.StartsWith("apr", StringComparison.Ordinal) => 4,
            _ when w is "mai" or "may" => 5,
            _ when w.StartsWith("jun", StringComparison.Ordinal) => 6,
            _ when w.StartsWith("jul", StringComparison.Ordinal) => 7,
            _ when w.StartsWith("aug", StringComparison.Ordinal) => 8,
            _ when w.StartsWith("sep", StringComparison.Ordinal) => 9,
            _ when w.StartsWith("okt", StringComparison.Ordinal) || w.StartsWith("oct", StringComparison.Ordinal) => 10,
            _ when w.StartsWith("nov", StringComparison.Ordinal) => 11,
            _ => 12,
        };
    }

    private static int? NumberValue(string word)
    {
        if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return word.ToLowerInvariant() switch
        {
            "ein" or "eine" or "einen" or "einem" or "einer" or "eins" or "a" or "an" or "one" => 1,
            "zwei" or "two" => 2,
            "drei" or "three" => 3,
            "vier" or "four" => 4,
            "fünf" or "fuenf" or "five" => 5,
            "sechs" or "six" => 6,
            "sieben" or "seven" => 7,
            "acht" or "eight" => 8,
            "neun" or "nine" => 9,
            "zehn" or "ten" => 10,
            "elf" or "eleven" => 11,
            "zwölf" or "zwoelf" or "twelve" => 12,
            _ => null,
        };
    }
}
