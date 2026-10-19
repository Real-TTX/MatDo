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
        ShowDone = done;
        CanManage = await projects.CanManageAsync(UserId, project);
        var q = TaskTools.Read(Request);
        Filtered = TaskTools.Active(q);

        var all = tasks.Visible(UserId).Where(t => t.ProjectId == id && t.ParentId == null);
        var rows = await tasks.RowsAsync(all.Where(t => !t.IsCompleted), q, Today);
        var keep = new Dictionary<string, string?> { ["id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["done"] = done ? "true" : null };
        Toolbar = TaskTools.Toolbar("project", "/Projects/View", q, await tasks.LabelChipsAsync(UserId), await all.CountAsync(t => !t.IsCompleted), rows.Count, keep);

        var reorder = !Filtered;
        var sections = await db.Sections.Where(s => s.ProjectId == id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).AsNoTracking().ToListAsync();
        var qa = Request.Query["qa"].ToString();
        Loose = new TaskListModel
        {
            Id = "list-loose",
            Rows = Filtered ? rows : rows.Where(r => r.SectionId is null || sections.All(s => s.Id != r.SectionId)).ToList(),
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
                    new TaskListModel { Id = $"list-s{s.Id}", Rows = rows.Where(r => r.SectionId == s.Id).ToList(), Today = Today, ReturnUrl = Here, ShowProject = false, Reorder = reorder },
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
}
