using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>What the menu shows: the counts of the views, the favourites, the personal projects and the projects of each team.</summary>
public sealed class MenuModel
{
    public long InboxId { get; init; }

    public int Inbox { get; init; }

    public int Today { get; init; }

    public int Overdue { get; init; }

    public int Upcoming { get; init; }

    public IReadOnlyList<ProjectInfo> Favorites { get; init; } = [];

    public IReadOnlyList<ProjectInfo> Personal { get; init; } = [];

    public IReadOnlyList<(long TeamId, string TeamName, IReadOnlyList<ProjectInfo> Projects)> Teams { get; init; } = [];
}

public sealed class MenuService(AppDbContext db, CurrentUser me, ProjectService projects, TaskService tasks)
{
    private MenuModel? _model;

    public async Task<MenuModel> GetAsync()
    {
        if (_model is not null)
        {
            return _model;
        }

        var userId = me.RequiredId;
        var today = me.Today;
        var all = await projects.ListAsync(userId);
        var inbox = all.FirstOrDefault(p => p.IsInbox) ?? await InboxInfoAsync(userId);
        var open = tasks.Visible(userId).Where(t => !t.IsCompleted);
        var mine = open.Where(t => t.AssigneeId == null || t.AssigneeId == userId);
        var week = today.AddDays(7);
        var counts = new
        {
            Today = await mine.CountAsync(t => t.DueDate <= today),
            Overdue = await mine.CountAsync(t => t.DueDate < today),
            Upcoming = await mine.CountAsync(t => t.DueDate > today && t.DueDate <= week),
        };

        var teamNames = await db.TeamMembers.Where(m => m.UserId == userId && m.Team != null).Select(m => new { m.TeamId, m.Team!.Name }).AsNoTracking().ToListAsync();
        _model = new MenuModel
        {
            InboxId = inbox.Id,
            Inbox = inbox.Open,
            Today = counts.Today,
            Overdue = counts.Overdue,
            Upcoming = counts.Upcoming,
            Favorites = all.Where(p => p.IsFavorite && !p.IsInbox).ToList(),
            Personal = all.Where(p => p.TeamId is null && !p.IsInbox).ToList(),
            Teams = teamNames.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => (t.TeamId, t.Name, (IReadOnlyList<ProjectInfo>)all.Where(p => p.TeamId == t.TeamId).ToList()))
                .ToList(),
        };
        return _model;
    }

    private async Task<ProjectInfo> InboxInfoAsync(long userId)
    {
        var inbox = await projects.InboxAsync(userId);
        return new ProjectInfo(inbox.Id, inbox.Name, inbox.Color, true, false, false, null, null, null, 0, 0);
    }
}
