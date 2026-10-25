using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages;

/// <summary>Today: what is overdue (with "reschedule to today") and what is due today, of every project the person sees (in a team project only their own and nobody's).</summary>
public sealed class TodayModel(AppDbContext db, TaskService tasks, QuickAddEditor editors) : AppPageModel
{
    public ToolbarModel Toolbar { get; private set; } = null!;

    public TaskListModel Overdue { get; private set; } = null!;

    public TaskListModel Due { get; private set; } = null!;

    public QuickAddModel Editor { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        var q = TaskTools.Read(Request, "due");
        var open = Mine().Where(t => !t.IsCompleted && t.DueDate <= Today);
        var rows = await tasks.RowsAsync(open, q with { Due = null }, Today);
        var labels = await tasks.LabelChipsAsync(UserId);
        Toolbar = TaskTools.Toolbar("today", "/Today", q, labels, await open.CountAsync(), rows.Count, defaultSort: "due", dueFilter: false);
        Overdue = new TaskListModel { Id = "today-overdue", Rows = rows.Where(r => r.DueDate < Today).ToList(), Today = Today, ReturnUrl = Here };
        Due = new TaskListModel { Id = "today-due", Rows = rows.Where(r => r.DueDate == Today).ToList(), Today = Today, ReturnUrl = Here, ShowDate = false };
        Editor = await editors.CreateAsync("qa-today", Here, due: Today, open: Request.Query["qa"] == "qa-today");
        QuickAddDefaults(new QuickAddContext(Due: Today));
    }

    /// <summary>Moves everything that is overdue to today (the times stay).</summary>
    public async Task<IActionResult> OnPostRescheduleAsync()
    {
        var overdue = await Mine().Where(t => !t.IsCompleted && t.DueDate < Today).ToListAsync();
        foreach (var t in overdue)
        {
            t.DueDate = Today;
        }

        await db.SaveChangesAsync();
        Flash(Text.T(overdue.Count == 1 ? "Today.MovedOne" : "Today.Moved", overdue.Count));
        return LocalRedirect("/Today");
    }

    private IQueryable<TaskItem> Mine() => tasks.Visible(UserId).Where(t => t.AssigneeId == null || t.AssigneeId == UserId);
}
