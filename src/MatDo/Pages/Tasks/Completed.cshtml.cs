using MatDo.Localization;
using System.Globalization;
using MatDo.Services;

namespace MatDo.Pages.Tasks;

/// <summary>
/// What was done, newest first: one row for each check-off (a repeating task has a row for each time it was done), with who did it and when. The newest check-off of a
/// task can be taken back from here (a completed task is open again, a repeating task goes back to that date); the older ones of a repeating task are fixed.
/// </summary>
public sealed class CompletedModel(ProjectService projects, TaskService tasks) : AppPageModel
{
    private const int PageSize = 50;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public TaskListModel List { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var q = TaskTools.Read(Request, "completed");
        if (q.Sort == "completed" && !Request.Query.ContainsKey("dir"))
        {
            q = q with { Descending = true };
        }

        var query = tasks.Done(UserId);
        var project = long.TryParse(Request.Query["project"], out var p) ? p : (long?)null;
        if (project is not null)
        {
            query = query.Where(t => t.ProjectId == project);
        }

        var tasksDone = await tasks.RowsAsync(query, q with { Due = null, Sort = "manual", Descending = false }, Today, 5000);
        var byId = tasksDone.ToDictionary(r => r.Id);
        var rows = (await tasks.CompletionsAsync(byId.Keys))
            .Select(e => byId[e.TaskId] with
            {
                IsCompleted = true,
                CompletedDate = e.At,
                DueDate = e.DueDate,
                AssigneeName = null,
                CompletionId = e.Id,
                CompletedBy = e.By,
                Locked = !e.Undoable,
            })
            .ToList();
        rows = Sort(rows, q);

        var all = await projects.ListAsync(UserId, includeArchived: true);
        Toolbar = TaskTools.Toolbar("done", "/Tasks/Completed", q, await tasks.LabelChipsAsync(UserId), await tasks.CompletionCountAsync(UserId), rows.Count,
            defaultSort: "completed", dueFilter: false, defaultDescending: true,
            extra: [new FilterField("project", Text.T("Tasks.Project"), all.Select(x => new FilterOption(x.Id.ToString(CultureInfo.InvariantCulture), x.IsInbox ? Text.T("Project.Inbox") : x.Name)).ToList(), project?.ToString(CultureInfo.InvariantCulture))],
            sorts: [new("completed", Text.T("Completed.Sort.CompletedAt")), new("name", Text.T("Tasks.Sort.Name")), new("priority", Text.T("Tasks.Sort.Priority"))]);

        var page = ListQuery.Page(Request);
        List = new TaskListModel { Id = "done-list", Rows = ListQuery.Slice(rows, page, PageSize).ToList(), Today = Today, ReturnUrl = Here };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = rows.Count, Url = Toolbar.UrlWithPage };
    }

    private static List<TaskRow> Sort(List<TaskRow> rows, TaskQuery q)
    {
        var sorted = q.Sort switch
        {
            "name" => rows.OrderBy(r => r.Content, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.CompletedDate),
            "priority" => rows.OrderBy(r => r.Priority).ThenBy(r => r.CompletedDate),
            _ => rows.OrderBy(r => r.CompletedDate).ThenBy(r => r.CompletionId),
        };
        var list = sorted.ToList();
        if (q.Descending)
        {
            list.Reverse();
        }

        return list;
    }
}
