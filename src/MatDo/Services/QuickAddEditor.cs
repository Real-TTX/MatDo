using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>One quick add editor on a page (a page can have several: the project, each section, each day of the upcoming view, the dialog of the shortcut Q).</summary>
public sealed class QuickAddModel
{
    public required string Id { get; init; }

    public required string ReturnUrl { get; init; }

    public long? DefaultProjectId { get; init; }

    public long? DefaultSectionId { get; init; }

    public DateOnly? DefaultDueDate { get; init; }

    public long? ParentId { get; init; }

    public bool Open { get; init; }

    /// <summary>The editor of the dialog: always open, no "add task" line.</summary>
    public bool InDialog { get; init; }

    public string AddLabel { get; init; } = Localization.Text.T("QuickAdd.Add");

    public string Placeholder { get; init; } = Localization.Text.T("QuickAdd.Placeholder");

    public IReadOnlyList<ProjectInfo> Projects { get; init; } = [];

    public IReadOnlyList<LabelChip> Labels { get; init; } = [];
}

/// <summary>Builds the editors of a request; the projects and labels of the person are read once per request.</summary>
public sealed class QuickAddEditor(AppDbContext db, CurrentUser me, ProjectService projects)
{
    private List<ProjectInfo>? _projects;
    private List<LabelChip>? _labels;

    public async Task<QuickAddModel> CreateAsync(string id, string returnUrl, long? projectId = null, long? sectionId = null, DateOnly? due = null, long? parentId = null,
        bool open = false, bool inDialog = false, string? addLabel = null)
    {
        _projects ??= await projects.ListAsync(me.RequiredId);
        _labels ??= (await db.Labels.Where(l => l.OwnerId == me.RequiredId).Select(l => new { l.Id, l.Name, l.Color }).AsNoTracking().ToListAsync())
            .Select(l => new LabelChip(l.Id, l.Name, l.Color)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new QuickAddModel
        {
            Id = id,
            ReturnUrl = returnUrl,
            DefaultProjectId = projectId ?? _projects.FirstOrDefault(p => p.IsInbox)?.Id,
            DefaultSectionId = sectionId,
            DefaultDueDate = due,
            ParentId = parentId,
            Open = open,
            InDialog = inDialog,
            AddLabel = addLabel ?? Localization.Text.T("QuickAdd.Add"),
            Placeholder = Localization.Text.T(parentId is null ? "QuickAdd.Placeholder" : "QuickAdd.SubPlaceholder"),
            Projects = _projects,
            Labels = _labels,
        };
    }
}
