namespace MatDo.Services;

/// <summary>
/// The empty state of a list (MatDesign LIST-11), where the rows would be. A list that has nothing yet offers its first item in it (<c>mat-empty--first</c>, the toolkit then hides
/// the same primary button of the actions below); a list where nothing matches the tools offers to reset them.
/// </summary>
public sealed record EmptyStateModel(string Icon, string Title, string Text, bool First, string? ActionUrl = null, string? ActionLabel = null, string? ResetUrl = null);
