using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>One project as the menu and the lists show it.</summary>
public sealed record ProjectInfo(long Id, string Name, string Color, bool IsInbox, bool IsFavorite, bool IsArchived, long? TeamId, string? TeamName, long? ParentId, int Open, int SortOrder);

/// <summary>
/// Who sees which project: a personal project (no team) only its owner, a project of a team every member of the team. Everything below a project
/// (sections, tasks, comments) follows the project: every query for tasks goes through <see cref="Visible"/>. A project nobody may see answers 404.
/// </summary>
public sealed class ProjectService(AppDbContext db)
{
    public static IReadOnlyList<(string Value, string Name)> Colors => ColorKeys.Select(c => (c.Value, Localization.Text.T("Color." + c.Key))).ToList();

    private static readonly IReadOnlyList<(string Value, string Key)> ColorKeys =
    [
        ("#d03d33", "Red"), ("#eb8909", "Orange"), ("#c9a400", "Yellow"), ("#7ecc49", "LightGreen"), ("#299438", "Green"), ("#158fad", "Teal"),
        ("#14aaf5", "SkyBlue"), ("#246fe0", "Blue"), ("#884dff", "Violet"), ("#af38eb", "Purple"), ("#e05194", "Pink"), ("#b8255f", "Berry"),
        ("#ff8d85", "Salmon"), ("#808080", "Grey"), ("#ccac93", "Taupe"),
    ];

    public IQueryable<Project> Visible(long userId)
        => db.Projects.Where(p => (p.TeamId == null && p.OwnerId == userId)
                                  || (p.TeamId != null && db.TeamMembers.Any(m => m.TeamId == p.TeamId && m.UserId == userId)));

    public IQueryable<long> VisibleIds(long userId) => Visible(userId).Select(p => p.Id);

    public async Task<Project> InboxAsync(long userId)
    {
        var inbox = await db.Projects.FirstOrDefaultAsync(p => p.OwnerId == userId && p.IsInbox && p.TeamId == null);
        if (inbox is null)
        {
            inbox = new Project { OwnerId = userId, Name = "Inbox", IsInbox = true, Color = "#246fe0", CreateUserId = userId };
            db.Projects.Add(inbox);
            await db.SaveChangesAsync();
        }

        return inbox;
    }

    public Task<Project?> FindAsync(long userId, long id) => Visible(userId).Include(p => p.Team).FirstOrDefaultAsync(p => p.Id == id);

    /// <summary>The projects of a person with the number of open tasks (the menu, the project list, the pickers).</summary>
    public async Task<List<ProjectInfo>> ListAsync(long userId, bool includeArchived = false)
    {
        var rows = await Visible(userId)
            .Where(p => includeArchived || !p.IsArchived)
            .Select(p => new
            {
                p.Id, p.Name, p.Color, p.IsInbox, p.IsFavorite, p.IsArchived, p.TeamId, TeamName = p.Team != null ? p.Team.Name : null, p.ParentId, p.SortOrder,
                Open = db.Tasks.Count(t => t.ProjectId == p.Id && !t.IsCompleted && t.ParentId == null),
            })
            .AsNoTracking()
            .ToListAsync();
        return rows
            .Select(r => new ProjectInfo(r.Id, r.Name, r.Color, r.IsInbox, r.IsFavorite, r.IsArchived, r.TeamId, r.TeamName, r.ParentId, r.Open, r.SortOrder))
            .OrderByDescending(p => p.IsInbox).ThenBy(p => p.TeamName ?? string.Empty).ThenBy(p => p.SortOrder).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>May the person change the project itself (name, colour, team, delete)? The owner, or the owner of its team.</summary>
    public async Task<bool> CanManageAsync(long userId, Project project)
    {
        if (project.TeamId is null)
        {
            return project.OwnerId == userId;
        }

        return project.OwnerId == userId
               || await db.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.UserId == userId && m.Role == TeamRole.Owner);
    }

    public async Task<int> NextSortOrderAsync(long userId)
        => (await Visible(userId).MaxAsync(p => (int?)p.SortOrder) ?? 0) + 1;

    /// <summary>The people a task of this project can be given to: the members of its team (a personal project: nobody but its owner).</summary>
    public async Task<List<(long Id, string Name)>> AssigneesAsync(Project project)
    {
        if (project.TeamId is null)
        {
            return [];
        }

        var rows = await db.TeamMembers.Where(m => m.TeamId == project.TeamId && m.User != null)
            .Select(m => new { m.UserId, m.User!.DisplayName }).AsNoTracking().ToListAsync();
        return rows.Select(r => (r.UserId, r.DisplayName)).OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
