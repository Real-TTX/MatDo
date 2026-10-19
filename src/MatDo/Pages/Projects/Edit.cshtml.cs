using MatDo.Localization;
using System.Globalization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Projects;

/// <summary>Create and change a project; delete it (with its tasks). A project of a team is managed by its owner or an owner of the team.</summary>
public sealed class EditModel(AppDbContext db, ProjectService projects, TeamService teams) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string Color { get; set; } = "#246fe0";

    [BindProperty] public string Team { get; set; } = string.Empty;

    [BindProperty] public bool IsFavorite { get; set; }

    [BindProperty] public bool IsArchived { get; set; }

    public List<Choice> TeamChoices { get; private set; } = [];

    public int OpenTasks { get; private set; }

    public bool IsNew => Id is null;

    public async Task<IActionResult> OnGetAsync(long? team)
    {
        if (Id is { } id)
        {
            var project = await LoadAsync(id);
            if (project is null)
            {
                return NotFound();
            }

            Name = project.Name;
            Color = project.Color;
            Team = project.TeamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            IsFavorite = project.IsFavorite;
            IsArchived = project.IsArchived;
            OpenTasks = await db.Tasks.CountAsync(t => t.ProjectId == id && !t.IsCompleted);
        }
        else if (team is { } t)
        {
            Team = t.ToString(CultureInfo.InvariantCulture);
        }

        await ChoicesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Project? project = null;
        if (Id is { } id)
        {
            project = await LoadAsync(id);
            if (project is null)
            {
                return NotFound();
            }
        }

        var name = Name.Trim();
        if (name.Length == 0)
        {
            ModelState.AddModelError(nameof(Name), Text.T("Common.NameRequired"));
        }
        else if (name.Equals("Eingang", StringComparison.OrdinalIgnoreCase) || name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Name), Text.T("Projects.InboxReserved"));
        }

        if (!Ui.IsColour(Color))
        {
            Color = "#808080";
        }

        long? teamId = null;
        if (long.TryParse(Team, out var t))
        {
            if (await teams.FindAsync(UserId, t) is null)
            {
                ModelState.AddModelError(nameof(Team), Text.T("Teams.NotFound"));
            }
            else
            {
                teamId = t;
            }
        }

        if (!ModelState.IsValid)
        {
            await ChoicesAsync();
            return Page();
        }

        if (project is null)
        {
            project = new Project { OwnerId = UserId, SortOrder = await projects.NextSortOrderAsync(UserId) };
            db.Projects.Add(project);
        }
        else if (project.TeamId != teamId)
        {
            // tasks given to someone who is not in the new team lose the assignee
            await db.Tasks.Where(x => x.ProjectId == project.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AssigneeId, (long?)null));
        }

        project.Name = name.Length > 120 ? name[..120] : name;
        project.Color = Color.ToLowerInvariant();
        project.TeamId = teamId;
        project.IsFavorite = IsFavorite;
        project.IsArchived = IsArchived;
        await db.SaveChangesAsync();
        Flash(IsNew ? Text.T("Common.CreatedNamed", project.Name) : Text.T("Common.Saved"));
        return LocalRedirect($"/Projects/View?id={project.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is not { } id || await LoadAsync(id) is not { } project)
        {
            return NotFound();
        }

        var tasks = await db.Tasks.Where(x => x.ProjectId == id).ToListAsync();
        db.Tasks.RemoveRange(tasks);
        db.Sections.RemoveRange(await db.Sections.Where(s => s.ProjectId == id).ToListAsync());
        await db.Webhooks.Where(w => w.ProjectId == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.ProjectId, (long?)null));
        db.Projects.Remove(project);
        await db.SaveChangesAsync();
        Flash(Text.T("Common.DeletedNamed", project.Name));
        return LocalRedirect("/Projects");
    }

    /// <summary>The project, when the person may manage it (the inbox is never edited here).</summary>
    private async Task<Project?> LoadAsync(long id)
    {
        var project = await projects.Visible(UserId).FirstOrDefaultAsync(p => p.Id == id && !p.IsInbox);
        return project is not null && await projects.CanManageAsync(UserId, project) ? project : null;
    }

    private async Task ChoicesAsync()
    {
        TeamChoices = [new Choice(string.Empty, Text.T("Projects.PersonalOnlyMe"))];
        TeamChoices.AddRange((await teams.ListAsync(UserId)).Select(x => new Choice(x.Id.ToString(CultureInfo.InvariantCulture), Text.T("Projects.TeamNamed", x.Name))));
    }
}
