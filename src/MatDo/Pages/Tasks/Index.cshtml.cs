using System.Globalization;
using MatDo.Services;

namespace MatDo.Pages.Tasks;

/// <summary>Filter and search: every open task the person sees, with the whole toolbar (project, label, priority, due, assigned) and pages.</summary>
public sealed class IndexModel(ProjectService projects, TaskService tasks) : AppPageModel
{
    private const int PageSize = 50;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public TaskListModel List { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var q = TaskTools.Read(Request, "due");
        var query = tasks.Visible(UserId).Where(t => !t.IsCompleted);
        var project = long.TryParse(Request.Query["project"], out var p) ? p : (long?)null;
        if (project is not null)
        {
            query = query.Where(t => t.ProjectId == project);
        }

        var who = ListQuery.Text(Request, "who");
        if (who == "me")
        {
            query = query.Where(t => t.AssigneeId == UserId);
        }
        else if (who == "others")
        {
            query = query.Where(t => t.AssigneeId != null && t.AssigneeId != UserId);
        }

        var total = tasks.Visible(UserId).Count(t => !t.IsCompleted);
        var rows = await tasks.RowsAsync(query, q, Today);
        var all = await projects.ListAsync(UserId);
        Toolbar = TaskTools.Toolbar("filter", "/Tasks", q, await tasks.LabelChipsAsync(UserId), total, rows.Count, defaultSort: "due", extra:
        [
            new FilterField("project", "Projekt", all.Select(x => new FilterOption(x.Id.ToString(CultureInfo.InvariantCulture), x.IsInbox ? "Eingang" : x.Name)).ToList(), project?.ToString(CultureInfo.InvariantCulture)),
            new FilterField("who", "Zugewiesen", [new("me", "An mich"), new("others", "An andere")], who),
        ]);

        var page = ListQuery.Page(Request);
        List = new TaskListModel { Id = "filter-list", Rows = ListQuery.Slice(rows, page, PageSize).ToList(), Today = Today, ReturnUrl = Here };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = rows.Count, Url = Toolbar.UrlWithPage };
    }
}
