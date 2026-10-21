using System.Text.Json;
using System.Text.Json.Serialization;
using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services.Auth;

/// <summary>
/// The data of a person under the GDPR: the export of everything that is about them (Art. 15 and 20, a JSON file), and the erasure of the account (Art. 17).
/// <para>
/// The export holds the account and its settings, the keywords, labels, personal projects with their sections and tasks (with sub-tasks, labels, comments), the
/// teams and the tasks of team projects the person created or was given, the comments they wrote there, the webhooks, the devices for notifications and the
/// sessions (and the person of MatIdentity the account is linked with). No secret: no password hash, no token of a session or a webhook, no ID token, no key of a device.
/// </para>
/// <para>
/// The erasure deletes for good (the account is not just hidden): the personal projects with everything in them, the labels, keywords, webhooks, devices,
/// sessions, the memberships and the comments the person wrote. What belongs to a team stays with the team: a team project the person created goes to
/// another member, a team keeps an owner (the longest member becomes one), a team without other members goes with its projects. A task given to the person
/// is given to nobody.
/// </para>
/// </summary>
public sealed class AccountDataService(AppDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The export as JSON (UTF-8).</summary>
    public async Task<byte[]> ExportAsync(long userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        var labels = await db.Labels.IgnoreQueryFilters().AsNoTracking().Where(l => l.OwnerId == userId).OrderBy(l => l.SortOrder)
            .Select(l => new { l.Id, l.Name, l.Color, l.IsFavorite, Deleted = l.UpdateState == SoftEntity.Deleted }).ToListAsync(ct);
        var labelNames = labels.ToDictionary(l => l.Id, l => l.Name);

        var personal = await db.Projects.IgnoreQueryFilters().AsNoTracking().Where(p => p.OwnerId == userId && p.TeamId == null).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var personalIds = personal.Select(p => p.Id).ToList();
        var sections = await db.Sections.IgnoreQueryFilters().AsNoTracking().Where(s => personalIds.Contains(s.ProjectId)).ToListAsync(ct);
        var memberships = await db.TeamMembers.AsNoTracking().Where(m => m.UserId == userId).Include(m => m.Team).ToListAsync(ct);
        var teamIds = memberships.Select(m => m.TeamId).ToList();
        var teamProjects = await db.Projects.IgnoreQueryFilters().AsNoTracking().Where(p => p.TeamId != null && teamIds.Contains(p.TeamId.Value)).ToDictionaryAsync(p => p.Id, ct);
        var teamProjectIds = teamProjects.Keys.ToList();

        var tasks = await db.Tasks.IgnoreQueryFilters().AsNoTracking().Include(t => t.Labels)
            .Where(t => personalIds.Contains(t.ProjectId)
                        || (teamProjectIds.Contains(t.ProjectId) && (t.CreateUserId == userId || t.AssigneeId == userId)))
            .OrderBy(t => t.ProjectId).ThenBy(t => t.SortOrder).ToListAsync(ct);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var comments = await db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => taskIds.Contains(c.TaskId) || c.AuthorId == userId).ToListAsync(ct);
        var sectionNames = sections.ToDictionary(s => s.Id, s => s.Name);

        object TaskOf(TaskItem t) => new
        {
            t.Id,
            t.ParentId,
            t.Content,
            t.Description,
            t.Priority,
            DueDate = t.DueDate,
            DueTime = t.DueTime,
            t.Recurrence,
            t.RecurrenceText,
            Deadline = t.DeadlineDate,
            Section = t.SectionId is { } s && sectionNames.TryGetValue(s, out var sn) ? sn : null,
            Labels = t.Labels.Select(l => labelNames.GetValueOrDefault(l.LabelId)).Where(n => n is not null).ToList(),
            AssignedToYou = t.AssigneeId == userId ? true : (bool?)null,
            t.IsCompleted,
            t.CompletedDate,
            t.Source,
            t.CreateDate,
            t.UpdateDate,
            Deleted = t.UpdateState == SoftEntity.Deleted ? true : (bool?)null,
            Comments = comments.Where(c => c.TaskId == t.Id).OrderBy(c => c.CreateDate)
                .Select(c => new { c.Text, Yours = c.AuthorId == userId, c.CreateDate, Deleted = c.UpdateState == SoftEntity.Deleted ? true : (bool?)null }).ToList(),
        };

        var export = new
        {
            Format = "MatDo export 1",
            ExportedAt = DateTime.UtcNow,
            Account = new
            {
                user.Email,
                user.DisplayName,
                user.Role,
                user.IsActive,
                user.TimeZone,
                Language = string.IsNullOrEmpty(user.Language) ? "browser" : user.Language,
                user.Theme,
                user.Scheme,
                user.AutoCreateProjects,
                user.AutoCreateLabels,
                user.ReminderMinutes,
                user.DigestTime,
                user.CreateDate,
                user.LastLoginDate,
                MatIdentitySubject = user.ExternalSubject,
            },
            Keywords = await db.Keywords.IgnoreQueryFilters().AsNoTracking().Where(k => k.UserId == userId).Select(k => new { k.Word, k.Kind }).ToListAsync(ct),
            Labels = labels.Select(l => new { l.Name, l.Color, l.IsFavorite, Deleted = l.Deleted ? true : (bool?)null }),
            Projects = personal.Select(p => new
            {
                p.Name,
                p.Color,
                p.IsInbox,
                p.IsFavorite,
                p.IsArchived,
                p.CreateDate,
                Deleted = p.UpdateState == SoftEntity.Deleted ? true : (bool?)null,
                Sections = sections.Where(s => s.ProjectId == p.Id).OrderBy(s => s.SortOrder).Select(s => s.Name).ToList(),
                Tasks = tasks.Where(t => t.ProjectId == p.Id).Select(TaskOf).ToList(),
            }),
            Teams = memberships.Select(m => new
            {
                Team = m.Team?.Name,
                m.Role,
                Joined = m.CreateDate,
                Tasks = tasks.Where(t => teamProjects.TryGetValue(t.ProjectId, out var tp) && tp.TeamId == m.TeamId)
                    .Select(t => new { Project = teamProjects[t.ProjectId].Name, Task = TaskOf(t) }).ToList(),
            }),
            CommentsOnOtherTasks = comments.Where(c => c.AuthorId == userId && !taskIds.Contains(c.TaskId)).Select(c => new { c.TaskId, c.Text, c.CreateDate }),
            Webhooks = await db.Webhooks.IgnoreQueryFilters().AsNoTracking().Where(w => w.UserId == userId)
                .Select(w => new { w.Name, w.IsActive, w.UseCount, w.LastUsedDate, w.CreateDate }).ToListAsync(ct),
            NotificationDevices = await db.PushDevices.AsNoTracking().Where(d => d.UserId == userId)
                .Select(d => new { d.Name, d.Language, d.CreateDate, d.LastSentDate }).ToListAsync(ct),
            Sessions = await db.Sessions.AsNoTracking().Where(s => s.UserId == userId)
                .Select(s => new { s.CreateDate, s.LastSeenDate, s.ExpiresDate, s.IsRevoked, s.UserAgent, s.IpAddress }).ToListAsync(ct),
        };

        return JsonSerializer.SerializeToUtf8Bytes(export, Json);
    }

    /// <summary>Erases the account for good (see the class). The caller checks the password and that it is not the last administrator.</summary>
    public async Task EraseAsync(long userId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // the teams the person is in: an owner stays (the longest member becomes one), a team that nobody else is in goes
        var emptyTeams = new List<long>();
        foreach (var membership in await db.TeamMembers.Where(m => m.UserId == userId).ToListAsync(ct))
        {
            var others = await db.TeamMembers.Where(m => m.TeamId == membership.TeamId && m.UserId != userId).OrderBy(m => m.Id).ToListAsync(ct);
            if (others.Count == 0)
            {
                emptyTeams.Add(membership.TeamId);
            }
            else if (others.All(m => m.Role != TeamRole.Owner))
            {
                others[0].Role = TeamRole.Owner;
            }
        }

        await db.SaveChangesAsync(ct);

        // a team project the person created belongs to the team (also of a team they left): it goes to an owner of the team, or with the team when nobody is left
        var teamProjects = await db.Projects.IgnoreQueryFilters().Where(p => p.TeamId != null && p.OwnerId == userId).ToListAsync(ct);
        foreach (var project in teamProjects.Where(p => !emptyTeams.Contains(p.TeamId!.Value)))
        {
            var heir = await db.TeamMembers.Where(m => m.TeamId == project.TeamId && m.UserId != userId)
                .OrderBy(m => m.Role == TeamRole.Owner ? 0 : 1).ThenBy(m => m.Id).Select(m => (long?)m.UserId).FirstOrDefaultAsync(ct);
            if (heir is { } next)
            {
                await db.Projects.IgnoreQueryFilters().Where(p => p.Id == project.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.OwnerId, next), ct);
            }
            else
            {
                emptyTeams.Add(project.TeamId!.Value);
            }
        }

        // the personal projects and those of the teams that go: with their sections and tasks (the database deletes those with them)
        var gone = await db.Projects.IgnoreQueryFilters()
            .Where(p => (p.TeamId == null && p.OwnerId == userId) || (p.TeamId != null && emptyTeams.Contains(p.TeamId.Value)))
            .Select(p => p.Id).ToListAsync(ct);
        await db.Projects.IgnoreQueryFilters().Where(p => p.ParentId != null && gone.Contains(p.ParentId.Value)).ExecuteUpdateAsync(s => s.SetProperty(p => p.ParentId, (long?)null), ct);
        await db.Projects.IgnoreQueryFilters().Where(p => gone.Contains(p.Id)).ExecuteDeleteAsync(ct);
        await db.Teams.IgnoreQueryFilters().Where(t => emptyTeams.Contains(t.Id)).ExecuteDeleteAsync(ct);

        // the account: the database deletes its sessions, keywords, memberships, labels, comments, webhooks and devices with it, and gives its tasks to nobody
        await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Is the person the last active administrator (who must not go, or nobody can run the installation)?</summary>
    public async Task<bool> IsLastAdminAsync(long userId)
        => await db.Users.AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin && u.IsActive)
           && !await db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin && u.IsActive);
}
