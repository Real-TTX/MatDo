using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Labels;

/// <summary>The open tasks with one label, from every project.</summary>
public sealed class ViewModel(AppDbContext db, TaskService tasks) : AppPageModel
{
    public Label Label { get; private set; } = null!;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public TaskListModel List { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var label = await db.Labels.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == UserId);
        if (label is null)
        {
            return NotFound();
        }

        Label = label;
        var q = TaskTools.Read(Request, "due");
        var query = tasks.Visible(UserId).Where(t => !t.IsCompleted && t.Labels.Any(l => l.LabelId == id));
        var rows = await tasks.RowsAsync(query, q, Today);
        Toolbar = TaskTools.Toolbar("label", "/Labels/View", q, [], await query.CountAsync(), rows.Count,
            new Dictionary<string, string?> { ["id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "due");
        List = new TaskListModel { Id = "label-list", Rows = rows, Today = Today, ReturnUrl = Here, Empty = "Keine offenen Aufgaben mit diesem Label." };
        return Page();
    }
}
