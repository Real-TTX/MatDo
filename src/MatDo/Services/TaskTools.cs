using System.Globalization;
using MatDo.Localization;

namespace MatDo.Services;

/// <summary>The toolbar of a task list (search, filters, sort) and the query it stands for: the same for every view, one code base.</summary>
public static class TaskTools
{
    public static TaskQuery Read(HttpRequest request, string defaultSort = "manual")
    {
        var sort = ListQuery.Text(request, "sort");
        if (sort is not ("manual" or "due" or "priority" or "name" or "created" or "completed"))
        {
            sort = defaultSort;
        }

        return new TaskQuery(
            ListQuery.Text(request, "q"),
            int.TryParse(request.Query["priority"], out var p) && p is >= 1 and <= 4 ? p : null,
            long.TryParse(request.Query["label"], out var l) ? l : null,
            ListQuery.Text(request, "due") is "none" or "overdue" or "today" or "week" ? request.Query["due"].ToString() : null,
            sort,
            ListQuery.Descending(request));
    }

    public static ToolbarModel Toolbar(string id, string action, TaskQuery q, IReadOnlyList<LabelChip> labels, int total, int matching,
        IReadOnlyDictionary<string, string?>? keep = null, string defaultSort = "manual", bool dueFilter = true, IReadOnlyList<FilterField>? extra = null,
        IReadOnlyList<SortOption>? sorts = null, bool defaultDescending = false)
    {
        var filters = new List<FilterField>
        {
            new("priority", Text.T("Tasks.Tools.Priority"), Ui.Priorities.Select(x => new FilterOption(x.Value.ToString(CultureInfo.InvariantCulture), x.Label)).ToList(), q.Priority?.ToString(CultureInfo.InvariantCulture)),
        };
        if (labels.Count > 0)
        {
            filters.Add(new FilterField("label", Text.T("Tasks.Tools.Label"), labels.Select(x => new FilterOption(x.Id.ToString(CultureInfo.InvariantCulture), x.Name)).ToList(), q.LabelId?.ToString(CultureInfo.InvariantCulture)));
        }

        if (dueFilter)
        {
            filters.Add(new FilterField("due", Text.T("Tasks.Tools.Due"), [new("overdue", Text.T("Tasks.Tools.Overdue")), new("today", Text.T("Common.Date.Today")), new("week", Text.T("Tasks.Tools.Next7")), new("none", Text.T("Tasks.Tools.NoDate"))], q.Due));
        }

        if (extra is not null)
        {
            filters.AddRange(extra);
        }

        return new ToolbarModel
        {
            Id = id,
            Action = action,
            Label = Text.T("Tasks.Noun"),
            Search = q.Search,
            SearchField = true,
            SearchPlaceholder = Text.T("Tasks.Tools.Search"),
            Filters = filters,
            SortOptions = sorts ?? [new("manual", Text.T("Tasks.Sort.Manual")), new("due", Text.T("Tasks.Sort.Due")), new("priority", Text.T("Tasks.Sort.Priority")), new("name", Text.T("Tasks.Sort.Name")), new("created", Text.T("Tasks.Sort.Created"))],
            Sort = q.Sort,
            Descending = q.Descending,
            DefaultSort = defaultSort,
            DefaultDescending = defaultDescending,
            Total = total,
            Matching = matching,
            Noun = Text.T("Tasks.Many"),
            NounOne = Text.T("Tasks.NounOne"),
            Keep = keep ?? new Dictionary<string, string?>(),
        };
    }

    /// <summary>Is any tool in effect (then a list is shown flat instead of in its groups, and the editors are hidden)?</summary>
    public static bool Active(TaskQuery q, string defaultSort = "manual")
        => !string.IsNullOrWhiteSpace(q.Search) || q.Priority is not null || q.LabelId is not null || q.Due is not null || q.Sort != defaultSort || q.Descending;
}
