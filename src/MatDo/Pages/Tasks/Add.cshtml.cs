using MatDo.Data;
using MatDo.Services;
using MatDo.Services.Parsing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Tasks;

/// <summary>
/// Where every quick add editor posts (and the checks of the rows when the script is off). After the post the browser goes back to the page it came from, with the
/// editor open again (<c>?qa=</c>), so the next task can be typed at once, like in Todoist.
/// </summary>
public sealed class AddModel(AppDbContext db, QuickAddService quick, TaskService tasks) : AppPageModel
{
    [BindProperty] public string Text { get; set; } = string.Empty;

    [BindProperty] public string Description { get; set; } = string.Empty;

    [BindProperty] public string ReturnUrl { get; set; } = string.Empty;

    [BindProperty] public string Editor { get; set; } = string.Empty;

    [BindProperty] public long? DefaultProjectId { get; set; }

    [BindProperty] public long? DefaultSectionId { get; set; }

    [BindProperty] public string DefaultDueDate { get; set; } = string.Empty;

    [BindProperty] public long? ParentId { get; set; }

    [BindProperty] public string Off { get; set; } = string.Empty;

    [BindProperty] public string DueDate { get; set; } = string.Empty;

    [BindProperty] public string DueTime { get; set; } = string.Empty;

    [BindProperty] public bool NoDate { get; set; }

    [BindProperty] public string Priority { get; set; } = string.Empty;

    [BindProperty] public string Labels { get; set; } = string.Empty;

    [BindProperty] public bool ProjectTouched { get; set; }

    public IActionResult OnGet() => LocalRedirect("/Today");

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        var (task, error) = await quick.CreateAsync(user, new QuickAddInput
        {
            Text = Text,
            Description = Description,
            ProjectId = ProjectTouched ? DefaultProjectId : null,
            DefaultProjectId = DefaultProjectId,
            DefaultSectionId = DefaultSectionId,
            DefaultDueDate = Dates.ParseIso(DefaultDueDate),
            ParentId = ParentId,
            DueDate = Dates.ParseIso(DueDate),
            DueTime = Dates.ParseTime(DueTime),
            NoDate = NoDate,
            Priority = int.TryParse(Priority, out var p) ? p : null,
            Labels = Labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Disabled = Api.AppApi.Off(Off),
        });

        if (task is null)
        {
            FlashError(error ?? Localization.Text.T("Tasks.AddFailed"));
        }
        else
        {
            var where = task.Project is { IsInbox: true } ? Localization.Text.T("Project.Inbox") : task.Project?.Name ?? string.Empty;
            Flash(task.DueDate is { } d ? Localization.Text.T("Tasks.AddedDue", task.Content, where, Dates.Relative(d, task.DueTime, Today)) : Localization.Text.T("Tasks.Added", task.Content, where));
        }

        return Back(WithEditor(ReturnUrl, Editor), "/Today");
    }

    public async Task<IActionResult> OnPostCompleteAsync(long id)
    {
        var (completed, next) = await tasks.CompleteAsync(UserId, id, Today);
        if (completed)
        {
            Flash(Localization.Text.T("Tasks.Completed"));
        }
        else if (next is { } n)
        {
            Flash(Localization.Text.T("Tasks.NextDue", Dates.Relative(n, Today)));
        }

        return Back(ReturnUrl, "/Today");
    }

    public async Task<IActionResult> OnPostReopenAsync(long id)
    {
        if (await tasks.ReopenAsync(UserId, id))
        {
            Flash(Localization.Text.T("Tasks.Reopened"));
        }

        return Back(ReturnUrl, "/Today");
    }

    /// <summary>The return address with <c>qa=&lt;editor&gt;</c>: the page opens that editor again.</summary>
    private static string WithEditor(string url, string editor)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(editor) || editor == "md-qa-dialog" || !System.Text.RegularExpressions.Regex.IsMatch(editor, "^[a-z0-9-]+$"))
        {
            return url;
        }

        var hash = url.IndexOf('#');
        var path = hash >= 0 ? url[..hash] : url;
        var parts = path.Split('?', 2);
        var query = parts.Length > 1 ? string.Join('&', parts[1].Split('&').Where(q => !q.StartsWith("qa=", StringComparison.Ordinal))) : string.Empty;
        query = string.IsNullOrEmpty(query) ? $"qa={editor}" : $"{query}&qa={editor}";
        return $"{parts[0]}?{query}#{editor}";
    }
}
