using MatDo.Localization;
using System.Globalization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using MatDo.Services.Parsing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Tasks;

public sealed record CommentView(long Id, string Author, bool Mine, string Text, DateTime Date);

/// <summary>
/// The page of a task: what it is (name, description), where it stands (project, section), when (date, time, repeating, deadline), how important (priority), labels and the
/// person it is given to; under it the sub-tasks (with their own editor) and the comments. Delete is on this page, apart from the other buttons.
/// </summary>
public sealed class EditModel(AppDbContext db, ProjectService projects, TaskService tasks, QuickAddService quick, QuickAddEditor editors) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string Description { get; set; } = string.Empty;

    [BindProperty] public long ProjectId { get; set; }

    [BindProperty] public string SectionId { get; set; } = string.Empty;

    [BindProperty] public string DueDate { get; set; } = string.Empty;

    [BindProperty] public string DueTime { get; set; } = string.Empty;

    [BindProperty] public string DueText { get; set; } = string.Empty;

    [BindProperty] public string Recurrence { get; set; } = string.Empty;

    [BindProperty] public string Deadline { get; set; } = string.Empty;

    [BindProperty] public int Priority { get; set; } = 4;

    [BindProperty] public string LabelIds { get; set; } = string.Empty;

    [BindProperty] public string AssigneeId { get; set; } = string.Empty;

    [BindProperty] public string CommentText { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)] public string ReturnUrl { get; set; } = string.Empty;

    public TaskItem Item { get; private set; } = null!;

    public TaskItem? Parent { get; private set; }

    public List<Choice> ProjectChoices { get; private set; } = [];

    public List<Choice> SectionChoices { get; private set; } = [];

    public List<Choice> RecurrenceChoices { get; private set; } = [];

    public List<Choice> LabelChoices { get; private set; } = [];

    public List<Choice> AssigneeChoices { get; private set; } = [];

    public List<Choice> PriorityChoices { get; } = Ui.Priorities.Select(p => new Choice(p.Value.ToString(CultureInfo.InvariantCulture), p.Label)).ToList();

    public TaskListModel SubTasks { get; private set; } = null!;

    public QuickAddModel SubEditor { get; private set; } = null!;

    public List<CommentView> Comments { get; private set; } = [];

    public string BackUrl => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : Parent is not null ? $"/Tasks/Edit?id={Parent.Id}" : $"/Projects/View?id={Item.ProjectId}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        Name = Item.Content;
        Description = Item.Description ?? string.Empty;
        ProjectId = Item.ProjectId;
        SectionId = Item.SectionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        DueDate = Item.DueDate is { } d ? Dates.Iso(d) : string.Empty;
        DueTime = Item.DueTime is { } t ? Dates.Time(t) : string.Empty;
        Recurrence = Item.Recurrence ?? string.Empty;
        Deadline = Item.DeadlineDate is { } dl ? Dates.Iso(dl) : string.Empty;
        Priority = Item.Priority;
        LabelIds = string.Join(", ", Item.Labels.Select(l => l.LabelId));
        AssigneeId = Item.AssigneeId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        await ChoicesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var content = Name.Trim();
        if (content.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), Text.T("Common.NameRequired"));
        }

        var project = Item.ParentId is null ? await projects.FindAsync(UserId, ProjectId) : Item.Project;
        if (project is null)
        {
            ModelState.AddModelError(nameof(ProjectId), Text.T("Projects.NotFound"));
        }

        DateOnly? due = Dates.ParseIso(DueDate);
        TimeOnly? time = Dates.ParseTime(DueTime);
        string? rule = string.IsNullOrWhiteSpace(Recurrence) ? null : Recurrence;
        string? ruleText = rule == Item.Recurrence ? Item.RecurrenceText : null;
        if (!string.IsNullOrWhiteSpace(DueText))
        {
            // the date in words, read like the quick add: "jeden Montag 9 Uhr", "übermorgen", "am 24.12. um 15 Uhr"
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
            var ctx = await quick.ContextAsync(user, new HashSet<TokenKind> { TokenKind.Project, TokenKind.Label, TokenKind.Priority, TokenKind.Section, TokenKind.Description, TokenKind.Deadline });
            var parse = QuickAddParser.Parse(DueText, ctx);
            if (parse.DueDate is null)
            {
                ModelState.AddModelError(nameof(DueText), Text.T("Tasks.DueText.NotUnderstood"));
            }
            else
            {
                due = parse.DueDate;
                time = parse.DueTime;
                rule = parse.Recurrence;
                ruleText = parse.RecurrenceText;
            }
        }

        if (rule is not null && !Services.Recurrence.IsValid(rule))
        {
            rule = null;
        }

        if (rule is not null && due is null)
        {
            due = Today;
        }

        if (!ModelState.IsValid)
        {
            await ChoicesAsync();
            return Page();
        }

        Item.Content = content.Length > 500 ? content[..500] : content;
        Item.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        if (Item.ProjectId != project!.Id)
        {
            Item.ProjectId = project.Id;
            Item.SectionId = null;
            Item.SortOrder = await tasks.NextSortOrderAsync(project.Id);
            // the sub-tasks go along, to any depth
            (await tasks.DescendantsAsync(Item.Id)).ForEach(c => { c.ProjectId = project.Id; c.SectionId = null; });
        }

        if (long.TryParse(SectionId, out var section) && await db.Sections.AnyAsync(s => s.Id == section && s.ProjectId == Item.ProjectId))
        {
            Item.SectionId = section;
        }
        else if (string.IsNullOrEmpty(SectionId))
        {
            Item.SectionId = null;
        }

        Item.DueDate = due;
        Item.DueTime = due is null ? null : time;
        Item.Recurrence = due is null ? null : rule;
        Item.RecurrenceText = Item.Recurrence is null ? null : ruleText;
        Item.DeadlineDate = Dates.ParseIso(Deadline);
        Item.Priority = Priority is >= 1 and <= 4 ? Priority : 4;
        Item.AssigneeId = long.TryParse(AssigneeId, out var a) && project.TeamId is not null && await db.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.UserId == a) ? a : null;
        await db.SaveChangesAsync();

        var ids = LabelIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToList();
        // labels of other members on a shared task stay; the person only sets their own
        var own = await db.TaskLabels.Where(l => l.TaskId == Item.Id && l.Label!.OwnerId == UserId).ToListAsync();
        db.TaskLabels.RemoveRange(own.Where(l => !ids.Contains(l.LabelId)));
        foreach (var id in ids.Where(i => own.All(o => o.LabelId != i)))
        {
            if (await db.Labels.AnyAsync(l => l.Id == id && l.OwnerId == UserId))
            {
                db.TaskLabels.Add(new TaskLabel { TaskId = Item.Id, LabelId = id });
            }
        }

        await db.SaveChangesAsync();
        Flash(Text.T("Common.Saved"));
        return LocalRedirect(BackUrl);
    }

    public async Task<IActionResult> OnPostCompleteAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var (completed, next) = await tasks.CompleteAsync(UserId, Id, Today);
        Flash(completed ? Text.T("Tasks.CompletedNamed", Item.Content) : next is { } n ? Text.T("Tasks.NextDue", Dates.Relative(n, Today)) : Text.T("Common.Saved"));
        return LocalRedirect(completed ? BackUrl : $"/Tasks/Edit?id={Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var back = Parent is not null ? $"/Tasks/Edit?id={Parent.Id}" : $"/Projects/View?id={Item.ProjectId}";
        await tasks.DeleteAsync(UserId, Id);
        Flash(Text.T("Common.DeletedNamed", Item.Content));
        return LocalRedirect(!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && !ReturnUrl.Contains($"id={Id}", StringComparison.Ordinal) ? ReturnUrl : back);
    }

    public async Task<IActionResult> OnPostCommentAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var text = CommentText.Trim();
        if (text.Length > 0)
        {
            db.Comments.Add(new TaskComment { TaskId = Id, AuthorId = UserId, Text = text.Length > 5000 ? text[..5000] : text });
            await db.SaveChangesAsync();
            Flash(Text.T("Comments.Saved"));
        }

        return LocalRedirect($"/Tasks/Edit?id={Id}#comments");
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(long commentId)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == Id && c.AuthorId == UserId);
        if (comment is not null)
        {
            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            Flash(Text.T("Comments.Deleted"));
        }

        return LocalRedirect($"/Tasks/Edit?id={Id}#comments");
    }

    private async Task<bool> LoadAsync()
    {
        var task = await tasks.FindAsync(UserId, Id);
        if (task is null)
        {
            return false;
        }

        Item = task;
        Parent = task.ParentId is { } p ? await tasks.Visible(UserId).AsNoTracking().FirstOrDefaultAsync(t => t.Id == p) : null;
        ViewData["ActiveProject"] = task.ProjectId;
        return true;
    }

    private async Task ChoicesAsync()
    {
        var all = await projects.ListAsync(UserId);
        ProjectChoices = all.Select(p => new Choice(p.Id.ToString(CultureInfo.InvariantCulture), p.IsInbox ? Text.T("Project.Inbox") : p.TeamName is null ? p.Name : $"{p.Name} ({p.TeamName})", null, p.IsInbox ? null : p.Color)).ToList();
        SectionChoices = [new Choice(string.Empty, Text.T("Tasks.NoSection"))];
        SectionChoices.AddRange(await db.Sections.Where(s => s.ProjectId == Item.ProjectId).OrderBy(s => s.SortOrder).Select(s => new Choice(s.Id.ToString(), s.Name, null, null)).ToListAsync());

        var due = Item.DueDate ?? Today;
        RecurrenceChoices = [new Choice(string.Empty, Text.T("Tasks.NoRecurrence"))];
        RecurrenceChoices.AddRange(Services.Recurrence.Presets(due).Select(p => new Choice(p.Value, p.Label)));
        if (Item.Recurrence is { } r && RecurrenceChoices.All(c => c.Value != r))
        {
            RecurrenceChoices.Add(new Choice(r, Services.Recurrence.Describe(r)));
        }

        LabelChoices = (await tasks.LabelChipsAsync(UserId)).Select(l => new Choice(l.Id.ToString(CultureInfo.InvariantCulture), l.Name, null, l.Color)).ToList();
        var project = Item.Project ?? await db.Projects.FirstAsync(p => p.Id == Item.ProjectId);
        AssigneeChoices = (await projects.AssigneesAsync(project)).Select(m => new Choice(m.Id.ToString(CultureInfo.InvariantCulture), m.Name)).ToList();
        if (AssigneeChoices.Count > 0)
        {
            AssigneeChoices.Insert(0, new Choice(string.Empty, Text.T("Tasks.Nobody")));
        }

        var children = tasks.Visible(UserId).Where(t => t.ParentId == Id);
        var rows = await tasks.RowsAsync(children, new TaskQuery(), Today);
        SubTasks = new TaskListModel
        {
            Id = "subtasks",
            Rows = rows.OrderBy(r => r.IsCompleted).ThenBy(r => r.SortOrder).ToList(),
            Today = Today,
            ReturnUrl = $"/Tasks/Edit?id={Id}",
            ShowProject = false,
            Reorder = true,
        };
        SubEditor = await editors.CreateAsync("qa-sub", $"/Tasks/Edit?id={Id}", projectId: Item.ProjectId, parentId: Id, open: Request.Query["qa"] == "qa-sub", addLabel: Text.T("Tasks.AddSubTask"));

        Comments = (await db.Comments.Where(c => c.TaskId == Id).OrderBy(c => c.CreateDate)
                .Select(c => new { c.Id, c.AuthorId, Author = c.Author!.DisplayName, c.Text, c.CreateDate }).AsNoTracking().ToListAsync())
            .Select(c => new CommentView(c.Id, c.Author, c.AuthorId == UserId, c.Text, c.CreateDate)).ToList();
    }
}
