using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Projects;

/// <summary>A project: the tasks without a section, then each section with its tasks, each with its editor; a new section at the end; the done tasks on request.</summary>
public sealed class ViewModel(AppDbContext db, ProjectService projects, TaskService tasks, QuickAddEditor editors) : AppPageModel
{
    public Project Project { get; private set; } = null!;

    public bool CanManage { get; private set; }

    public ToolbarModel Toolbar { get; private set; } = null!;

    public bool Filtered { get; private set; }

    public bool ShowDone { get; private set; }

    public TaskListModel Loose { get; private set; } = null!;

    public QuickAddModel? LooseEditor { get; private set; }

    public List<(Section Section, TaskListModel List, QuickAddModel Editor)> Sections { get; } = [];

    public TaskListModel? Done { get; private set; }

    [BindProperty] public string SectionName { get; set; } = string.Empty;

    public string Title => Project.IsInbox ? Text.T("Project.Inbox") : Project.Name;

    public async Task<IActionResult> OnGetAsync(long id, bool done = false)
    {
        var project = await projects.FindAsync(UserId, id);
        if (project is null)
        {
            return NotFound();
        }

        Project = project;
        QuickAddDefaults(new QuickAddContext(ProjectId: project.Id));
        ShowDone = done;
        CanManage = await projects.CanManageAsync(UserId, project);
        var q = TaskTools.Read(Request);
        Filtered = TaskTools.Active(q);

        // the sub-tasks too: without a tool in effect they stand under their task (a tree, like Todoist), with one they are rows like the others
        var all = tasks.Visible(UserId).Where(t => t.ProjectId == id);
        var rows = await tasks.RowsAsync(all.Where(t => !t.IsCompleted), q, Today);
        if (!Filtered)
        {
            rows = TaskService.Tree(rows);
        }
        var keep = new Dictionary<string, string?> { ["id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        // the completed tasks are a filter like the others (a chip, reset with them)
        FilterField[] showDone = [new("done", Text.T("Projects.View.Done"), [new("true", Text.T("Projects.View.ShowDone"))], done ? "true" : null)];
        Toolbar = TaskTools.Toolbar("project", "/Projects/View", q, await tasks.LabelChipsAsync(UserId), await all.CountAsync(t => !t.IsCompleted && t.ParentId == null), Filtered ? rows.Count : rows.Count(r => r.Depth == 0), keep, extra: showDone);

        var reorder = !Filtered;
        var sections = await db.Sections.Where(s => s.ProjectId == id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).AsNoTracking().ToListAsync();
        var qa = Request.Query["qa"].ToString();
        Loose = new TaskListModel
        {
            Id = "list-loose",
            Rows = Filtered ? rows : InSection(rows, null, sections),
            Today = Today,
            ReturnUrl = Here,
            ShowProject = false,
            Reorder = reorder,
        };

        if (!Filtered)
        {
            LooseEditor = await editors.CreateAsync("qa-loose", Here, projectId: id, open: qa == "qa-loose");
            foreach (var s in sections)
            {
                var editorId = $"qa-s{s.Id}";
                Sections.Add((s,
                    new TaskListModel { Id = $"list-s{s.Id}", Rows = InSection(rows, s.Id, sections), Today = Today, ReturnUrl = Here, ShowProject = false, Reorder = reorder },
                    await editors.CreateAsync(editorId, Here, projectId: id, sectionId: s.Id, open: qa == editorId)));
            }
        }

        if (done)
        {
            var finished = await tasks.RowsAsync(all.Where(t => t.IsCompleted), q with { Sort = "completed", Descending = true }, Today, 200);
            Done = new TaskListModel { Id = "list-done", Rows = finished, Today = Today, ReturnUrl = Here, ShowProject = false, Empty = Text.T("Projects.View.NothingDone") };
        }

        ViewData["ActiveProject"] = id;
        return Page();
    }

    public async Task<IActionResult> OnPostAddSectionAsync(long id)
    {
        var project = await projects.FindAsync(UserId, id);
        if (project is null)
        {
            return NotFound();
        }

        var name = SectionName.Trim();
        if (name.Length == 0)
        {
            FlashError(Text.T("Common.NameRequired"));
        }
        else
        {
            var order = (await db.Sections.Where(s => s.ProjectId == id).MaxAsync(s => (int?)s.SortOrder) ?? 0) + 1;
            db.Sections.Add(new Section { ProjectId = id, Name = name.Length > 120 ? name[..120] : name, SortOrder = order });
            await db.SaveChangesAsync();
            Flash(Text.T("Common.CreatedNamed", name));
        }

        return LocalRedirect($"/Projects/View?id={id}");
    }

    /// <summary>The rows of the tree whose top task stands in a section (null: in none, or in one that is gone); a sub-task goes with its task.</summary>
    private static List<TaskRow> InSection(List<TaskRow> tree, long? section, List<Section> sections)
    {
        var result = new List<TaskRow>();
        var take = false;
        foreach (var r in tree)
        {
            if (r.Depth == 0)
            {
                var own = r.SectionId is { } sid && sections.Any(s => s.Id == sid) ? r.SectionId : null;
                take = own == section;
            }

            if (take)
            {
                result.Add(r);
            }
        }

        return result;
    }
}
