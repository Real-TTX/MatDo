namespace MatDo.Services;

/// <summary>One list of tasks on a page (a page can have several: the sections of a project, the days of the upcoming view).</summary>
public sealed class TaskListModel
{
    public required string Id { get; init; }

    public required IReadOnlyList<TaskRow> Rows { get; init; }

    public required DateOnly Today { get; init; }

    public required string ReturnUrl { get; init; }

    /// <summary>The project of each task is shown at its right (not in the list of one project).</summary>
    public bool ShowProject { get; init; } = true;

    /// <summary>The rows can be moved by drag and drop (a list in manual order).</summary>
    public bool Reorder { get; init; }

    /// <summary>The date is left out where the list is the date already (a day of the upcoming view).</summary>
    public bool ShowDate { get; init; } = true;

    /// <summary>What the list says when it is empty; null: nothing at all.</summary>
    public string? Empty { get; init; }
}
