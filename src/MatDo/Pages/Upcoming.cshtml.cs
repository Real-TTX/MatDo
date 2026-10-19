using MatDo.Data;
using MatDo.Services;

namespace MatDo.Pages;

/// <summary>Upcoming: two weeks day by day (each day with its editor, which gives a new task that day), the overdue tasks above them; <c>?from=</c> moves the window.</summary>
public sealed class UpcomingModel(TaskService tasks, QuickAddEditor editors) : AppPageModel
{
    public const int Days = 14;

    public DateOnly From { get; private set; }

    public ToolbarModel Toolbar { get; private set; } = null!;

    public TaskListModel? Overdue { get; private set; }

    public List<(DateOnly Day, TaskListModel List, QuickAddModel? Editor)> Groups { get; } = [];

    public bool Filtered { get; private set; }

    public async Task OnGetAsync(string? from)
    {
        From = Dates.ParseIso(from) is { } f && f > Today ? f : Today;
        var to = From.AddDays(Days - 1);
        var q = TaskTools.Read(Request, "due");
        Filtered = TaskTools.Active(q, "due");

        var mine = tasks.Visible(UserId).Where(t => !t.IsCompleted && (t.AssigneeId == null || t.AssigneeId == UserId));
        var window = mine.Where(t => t.DueDate >= From && t.DueDate <= to);
        var rows = await tasks.RowsAsync(window, q with { Due = null }, Today);
        var keep = new Dictionary<string, string?> { ["from"] = From == Today ? null : Dates.Iso(From) };
        Toolbar = TaskTools.Toolbar("upcoming", "/Upcoming", q, await tasks.LabelChipsAsync(UserId), rows.Count, rows.Count, keep, "due", dueFilter: false);

        if (From == Today)
        {
            var overdue = await tasks.RowsAsync(mine.Where(t => t.DueDate < Today), q with { Due = null }, Today);
            if (overdue.Count > 0)
            {
                Overdue = new TaskListModel { Id = "up-overdue", Rows = overdue, Today = Today, ReturnUrl = Here };
            }
        }

        for (var day = From; day <= to; day = day.AddDays(1))
        {
            var d = day;
            var id = $"qa-{Dates.Iso(d)}";
            var list = new TaskListModel { Id = $"day-{Dates.Iso(d)}", Rows = rows.Where(r => r.DueDate == d).ToList(), Today = Today, ReturnUrl = Here, ShowDate = false };
            var editor = Filtered ? null : await editors.CreateAsync(id, Here, due: d, open: Request.Query["qa"] == id);
            if (!Filtered || list.Rows.Count > 0)
            {
                Groups.Add((d, list, editor));
            }
        }
    }
}
