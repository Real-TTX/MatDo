using MatDo.Data;
using MatDo.Localization;
using MatDo.Services.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>
/// What the editor (or the webhook) sends. <see cref="Text"/> is read by the parser; the <c>Default*</c> values are where the editor stands (the project page, the day of the
/// upcoming view) and lose against what the text says; the other values were chosen by hand in the editor and win.
/// </summary>
public sealed class QuickAddInput
{
    public string Text { get; init; } = string.Empty;

    public string? Description { get; init; }

    public long? DefaultProjectId { get; init; }

    public long? DefaultSectionId { get; init; }

    public DateOnly? DefaultDueDate { get; init; }

    public long? ParentId { get; init; }

    public long? ProjectId { get; init; }

    public DateOnly? DueDate { get; init; }

    public TimeOnly? DueTime { get; init; }

    /// <summary>The date was removed by hand: no date, whatever the text says.</summary>
    public bool NoDate { get; init; }

    public int? Priority { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];

    public long? AssigneeId { get; init; }

    public IReadOnlySet<TokenKind> Disabled { get; init; } = new HashSet<TokenKind>();

    public TaskSource Source { get; init; } = TaskSource.App;
}

/// <summary>A preview of what the quick add will create: the parse plus the names it resolved (for the chips of the editor).</summary>
public sealed record QuickAddPreview(ParseResult Parse, long? ProjectId, string? ProjectName, string? ProjectColor, bool ProjectIsNew, string? SectionName);

/// <summary>Turns a line of text into a task: the parser with the projects, labels, sections and keywords of the person. The editor and the webhook both come here.</summary>
public sealed class QuickAddService(AppDbContext db, ProjectService projects, TaskService tasks)
{
    public async Task<ParseContext> ContextAsync(User user, IReadOnlySet<TokenKind>? disabled = null)
    {
        var visible = await projects.Visible(user.Id).Where(p => !p.IsArchived).Select(p => new { p.Id, p.Name, p.IsInbox }).AsNoTracking().ToListAsync();
        var ids = visible.Select(p => p.Id).ToList();
        var sections = await db.Sections.Where(s => ids.Contains(s.ProjectId)).Select(s => new { s.ProjectId, s.Name }).AsNoTracking().ToListAsync();
        var labels = await db.Labels.Where(l => l.OwnerId == user.Id).Select(l => l.Name).AsNoTracking().ToListAsync();
        var keywords = await db.Keywords.Where(k => k.UserId == user.Id).Select(k => new { k.Kind, k.Word }).AsNoTracking().ToListAsync();

        var bySection = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var p in visible)
        {
            var names = sections.Where(s => s.ProjectId == p.Id).Select(s => s.Name).ToList();
            bySection[p.Name.ToLowerInvariant()] = names;
            if (p.IsInbox)
            {
                bySection[string.Empty] = names;
            }
        }

        return new ParseContext
        {
            Today = Dates.Today(user.TimeZone),
            Now = Dates.Now(user.TimeZone),
            Projects = visible.Where(p => !p.IsInbox).Select(p => p.Name).Append("Eingang").Append("Inbox").Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Labels = labels,
            Sections = bySection,
            Keywords = keywords.GroupBy(k => k.Kind).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(k => k.Word).ToList()),
            AllowNewProjects = user.AutoCreateProjects,
            AllowNewLabels = user.AutoCreateLabels,
            Disabled = disabled ?? new HashSet<TokenKind>(),
        };
    }

    public async Task<QuickAddPreview> PreviewAsync(User user, string text, IReadOnlySet<TokenKind>? disabled = null)
    {
        var ctx = await ContextAsync(user, disabled);
        var parse = QuickAddParser.Parse(text, ctx);
        var project = await ResolveProjectAsync(user.Id, parse.Project);
        var name = project is { IsInbox: true } ? Text.T("Project.Inbox") : project?.Name ?? (parse.ProjectIsNew ? parse.Project : null);
        return new QuickAddPreview(parse, project?.Id, name, project?.Color, project is null && parse.ProjectIsNew, parse.Section);
    }

    public async Task<(TaskItem? Task, string? Error)> CreateAsync(User user, QuickAddInput input)
    {
        var ctx = await ContextAsync(user, input.Disabled);
        var parse = QuickAddParser.Parse(input.Text, ctx);
        if (string.IsNullOrWhiteSpace(parse.Title))
        {
            return (null, Text.T("Tasks.NameRequired"));
        }

        // the project: chosen by hand, else what the text says, else where the editor stands, else the inbox
        Project? project = null;
        if (input.ProjectId is { } chosen)
        {
            project = await projects.FindAsync(user.Id, chosen);
        }

        if (project is null && parse.Project is not null)
        {
            project = await ResolveProjectAsync(user.Id, parse.Project);
            if (project is null && parse.ProjectIsNew && user.AutoCreateProjects)
            {
                project = new Project { OwnerId = user.Id, Name = Trim(parse.Project, 120), Color = "#808080", SortOrder = await projects.NextSortOrderAsync(user.Id) };
                db.Projects.Add(project);
                await db.SaveChangesAsync();
            }
        }

        if (project is null && input.DefaultProjectId is { } fallback)
        {
            project = await projects.FindAsync(user.Id, fallback);
        }

        TaskItem? parent = null;
        if (input.ParentId is { } parentId)
        {
            parent = await tasks.Visible(user.Id).FirstOrDefaultAsync(t => t.Id == parentId);
            if (parent is null)
            {
                return (null, Text.T("Tasks.ParentMissing"));
            }

            project = await projects.FindAsync(user.Id, parent.ProjectId);
        }

        project ??= await projects.InboxAsync(user.Id);

        long? sectionId = null;
        if (parent is not null)
        {
            sectionId = parent.SectionId;
        }
        else if (parse.Section is not null)
        {
            sectionId = await db.Sections.Where(s => s.ProjectId == project.Id && s.Name.ToLower() == parse.Section.ToLower()).Select(s => (long?)s.Id).FirstOrDefaultAsync();
        }
        else if (input.DefaultSectionId is { } section && await db.Sections.AnyAsync(s => s.Id == section && s.ProjectId == project.Id))
        {
            sectionId = section;
        }

        var task = new TaskItem
        {
            ProjectId = project.Id,
            SectionId = sectionId,
            ParentId = parent?.Id,
            Content = Trim(parse.Title, 500),
            Description = Combine(input.Description, parse.Description),
            Priority = input.Priority is >= 1 and <= 4 ? input.Priority.Value : parse.Priority ?? 4,
            Recurrence = parse.Recurrence,
            RecurrenceText = parse.RecurrenceText is null ? null : Trim(parse.RecurrenceText, 120),
            DeadlineDate = parse.Deadline,
            Source = input.Source,
            SortOrder = await tasks.NextSortOrderAsync(project.Id),
            CreateUserId = user.Id,
        };

        if (input.NoDate)
        {
            task.Recurrence = null;
            task.RecurrenceText = null;
        }
        else if (input.DueDate is not null)
        {
            task.DueDate = input.DueDate;
            task.DueTime = input.DueTime;
        }
        else
        {
            task.DueDate = parse.DueDate ?? input.DefaultDueDate;
            task.DueTime = parse.DueTime ?? input.DueTime;
        }

        if (input.AssigneeId is { } assignee && project.TeamId is not null
            && await db.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.UserId == assignee))
        {
            task.AssigneeId = assignee;
        }

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        var labels = parse.Labels.Concat(input.Labels).ToList();
        if (labels.Count > 0)
        {
            await tasks.SetLabelsAsync(task, user.Id, labels, user.AutoCreateLabels || input.Labels.Count > 0);
        }

        task.Project = project;
        return (task, null);
    }

    private async Task<Project?> ResolveProjectAsync(long userId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.Equals("Eingang", StringComparison.OrdinalIgnoreCase) || name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
        {
            return await projects.InboxAsync(userId);
        }

        var lower = name.ToLower();
        return await projects.Visible(userId).Where(p => !p.IsArchived && !p.IsInbox && p.Name.ToLower() == lower)
            .OrderBy(p => p.TeamId != null).ThenBy(p => p.Id).FirstOrDefaultAsync();
    }

    private static string? Combine(string? a, string? b)
    {
        var parts = new[] { a?.Trim(), b?.Trim() }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? null : Trim(string.Join("\n\n", parts), 10000);
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max];
}
