using MatDo.Controls;

namespace MatDo.Services;

/// <summary>
/// The panel of a date (partial _DuePop, script pick.js): the quick choices, the time, the month that is always shown and, where it is a date of a task, the repeat
/// and the date in words. The same control in the quick add (date), on the page of a task (date and deadline) – one code base.
/// </summary>
/// <param name="Id">The id of the panel (the button that opens it points to it with aria-controls).</param>
/// <param name="Label">The name of the panel for screen readers ("Choose a date").</param>
/// <param name="NoneLabel">The choice that takes the date away ("No date", "No deadline").</param>
/// <param name="Time">With the time (not for a deadline).</param>
/// <param name="Repeats">The rules to repeat (empty = no repeat in this panel).</param>
/// <param name="TextName">The name of the field of the date in words ("every Monday 9am"), read by the server when it saves; null = none.</param>
public sealed record DuePopModel(string Id, string Label, string NoneLabel, bool Time = true, IReadOnlyList<Choice>? Repeats = null, string? TextName = null);

/// <summary>The panel of the priorities (partial _PriorityPop): p1 to p4 with their flags.</summary>
public sealed record PriorityPopModel(string Id, string Label);
