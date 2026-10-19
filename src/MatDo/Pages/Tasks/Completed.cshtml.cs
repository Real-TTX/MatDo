using MatDo.Localization;
using System.Globalization;
using MatDo.Services;

namespace MatDo.Pages.Tasks;

/// <summary>What was done, newest first; a task can be opened again from here.</summary>
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

        var query = tasks.Visible(UserId).Where(t => t.IsCompleted);
        var project = long.TryParse(Request.Query["project"], out var p) ? p : (long?)null;
        if (project is not null)
        {
            query = query.Where(t => t.ProjectId == project);
        }

        var rows = await tasks.RowsAsync(query, q with { Due = null }, Today, 5000);
        var all = await projects.ListAsync(UserId, includeArchived: true);
        Toolbar = TaskTools.Toolbar("done", "/Tasks/Completed", q, await tasks.LabelChipsAsync(UserId), tasks.Visible(UserId).Count(t => t.IsCompleted), rows.Count,
            defaultSort: "completed", dueFilter: false, defaultDescending: true,
            extra: [new FilterField("project", Text.T("Tasks.Project"), all.Select(x => new FilterOption(x.Id.ToString(CultureInfo.InvariantCulture), x.IsInbox ? Text.T("Project.Inbox") : x.Name)).ToList(), project?.ToString(CultureInfo.InvariantCulture))],
            sorts: [new("completed", Text.T("Completed.Sort.CompletedAt")), new("name", Text.T("Tasks.Sort.Name")), new("priority", Text.T("Tasks.Sort.Priority"))]);

        var page = ListQuery.Page(Request);
        List = new TaskListModel { Id = "done-list", Rows = ListQuery.Slice(rows, page, PageSize).ToList(), Today = Today, ReturnUrl = Here };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = rows.Count, Url = Toolbar.UrlWithPage };
    }
}
