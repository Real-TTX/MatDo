using MatDo.Data;

namespace MatDo.Services;

/// <summary>The kinds of keywords as a person reads them, with an example each.</summary>
public static class KeywordKinds
{
    public static IReadOnlyList<(KeywordKind Kind, string Name, string Example)> All { get; } =
    [
        (KeywordKind.Project, "Projekt", "„Projekt EIMER Angebot schreiben“ → Projekt EIMER"),
        (KeywordKind.Label, "Label", "„Label Einkauf Milch“ → Label Einkauf"),
        (KeywordKind.Priority, "Priorität", "„Priorität 1“, „Priorität hoch“ → P1"),
        (KeywordKind.Section, "Abschnitt", "„Abschnitt Planung“ → Abschnitt im erkannten Projekt"),
        (KeywordKind.Deadline, "Frist", "„Frist Freitag“ → Frist am Freitag"),
        (KeywordKind.Description, "Beschreibung", "„Beschreibung Belege sammeln“ → der Rest wird die Beschreibung"),
    ];

    public static string Name(KeywordKind kind) => All.FirstOrDefault(k => k.Kind == kind).Name ?? kind.ToString();

    public static string Example(KeywordKind kind) => All.FirstOrDefault(k => k.Kind == kind).Example ?? string.Empty;
}
