using MatDo.Data;
using MatDo.Localization;

namespace MatDo.Services;

/// <summary>The kinds of keywords as a person reads them, with an example each (in the language of the interface).</summary>
public static class KeywordKinds
{
    public static IReadOnlyList<KeywordKind> All { get; } =
        [KeywordKind.Project, KeywordKind.Label, KeywordKind.Priority, KeywordKind.Section, KeywordKind.Deadline, KeywordKind.Description];

    public static string Name(KeywordKind kind) => Text.T($"Keywords.Kind.{kind}");

    public static string Example(KeywordKind kind) => Text.T($"Keywords.Kind.{kind}.Example");
}
