using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace MatDo.Services;

/// <summary>Small helpers for the markup of the toolkit: icons, the logo, names, priorities.</summary>
public static partial class Ui
{
    public const string AppId = "matdo";
    public const string AppName = "MatDo";

    /// <summary>The accent (a red like Todoist's, a little darker and warmer: white text on it reaches 4.78:1).</summary>
    public const string Accent = "#d03d33";

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColourRegex();

    /// <summary>An icon from the page's inline sprite (the toolkit's and the app's own).</summary>
    public static IHtmlContent Icon(string name, string? extraClass = null, string? colour = null)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not an icon name.", nameof(name));
        }

        var cls = string.IsNullOrEmpty(extraClass) ? "mat-icon" : $"mat-icon {extraClass}";
        var data = IsColour(colour) ? $" data-colour=\"{colour}\"" : string.Empty;
        return new HtmlString($"<svg class=\"{cls}\"{data} aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-{name}\"></use></svg>");
    }

    public static bool IsColour(string? value) => value is not null && ColourRegex().IsMatch(value);

    /// <summary>The logo of the menu and the sign-in: the mark of MatDo (a check in a rounded square on the accent).</summary>
    public static IHtmlContent Logo(string size = "md")
        => new HtmlString($"<span class=\"mat-logo mat-logo--{size} md-logo\" role=\"img\" aria-label=\"{AppName}\"><svg class=\"mat-icon\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-matdo\"></use></svg></span>");

    public static string Initials(string name)
    {
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return letters.Length == 0 ? "?" : letters;
    }

    public static string PriorityName(int p) => p switch
    {
        1 => Localization.Text.T("Priority.1"),
        2 => Localization.Text.T("Priority.2"),
        3 => Localization.Text.T("Priority.3"),
        _ => Localization.Text.T("Priority.4"),
    };

    public static string PriorityClass(int p) => p is >= 1 and <= 3 ? $"md-p{p}" : "md-p4";

    public static IReadOnlyList<(int Value, string Label)> Priorities => [(1, PriorityName(1)), (2, PriorityName(2)), (3, PriorityName(3)), (4, PriorityName(4))];

    /// <summary>The label of a due date in a row, and whether it is overdue / today / soon (the class of its colour).</summary>
    public static (string Text, string Class) Due(DateOnly date, TimeOnly? time, DateOnly today)
    {
        var text = Dates.Relative(date, time, today);
        var cls = date < today ? "md-due--overdue" : date == today ? "md-due--today" : date.DayNumber - today.DayNumber == 1 ? "md-due--tomorrow" : date.DayNumber - today.DayNumber < 7 ? "md-due--week" : "md-due--later";
        return (text, cls);
    }
}
