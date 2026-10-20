using MatDo.Data;
using MatDo.Localization;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services.Notifications;

/// <summary>
/// Sends the notifications of the app, once a minute:
/// - the reminder of a task with a time, <see cref="User.ReminderMinutes"/> before it (at most 30 minutes late: a server that was down does not send old ones),
///   to the person it is assigned to, else to the owner of the personal project or the person who created the team task; once per due moment
///   (<see cref="TaskItem.RemindedFor"/>), so a new date or time, or the next date of a repeating task, reminds again;
/// - the daily overview at <see cref="User.DigestTime"/> (up to two hours late), with the tasks of today and the overdue ones, once a day and only when there are any.
/// Only people with a device (<see cref="PushDevice"/>) are looked at.
/// </summary>
public sealed class NotificationService(IServiceScopeFactory scopes, ILogger<NotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan Late = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan DigestLate = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "The notifications failed; they are tried again in a minute.");
                }

                // at the start of the next minute: a reminder for 09:00 goes at 09:00, not at 09:00:59
                var now = DateTime.UtcNow;
                await Task.Delay(TimeSpan.FromSeconds(61 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // the app stops
        }
    }

    public async Task RunOnceAsync(DateTime now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<PushService>();

        var people = await db.Users.Where(u => u.IsActive && db.PushDevices.Any(d => d.UserId == u.Id)).ToDictionaryAsync(u => u.Id, ct);
        if (people.Count == 0)
        {
            return;
        }

        await RemindAsync(db, push, people, now, ct);
        await DigestAsync(db, push, people.Values, now, ct);
    }

    private static async Task RemindAsync(AppDbContext db, PushService push, Dictionary<long, User> people, DateTime now, CancellationToken ct)
    {
        // the dates are floating (in the zone of each person): two days around today cover every zone
        var from = DateOnly.FromDateTime(now).AddDays(-2);
        var to = DateOnly.FromDateTime(now).AddDays(2);
        var tasks = await db.Tasks.Include(t => t.Project)
            .Where(t => !t.IsCompleted && t.DueDate != null && t.DueTime != null && t.DueDate >= from && t.DueDate <= to && !t.Project!.IsArchived)
            .ToListAsync(ct);

        foreach (var task in tasks)
        {
            if (Recipient(task) is not { } userId || !people.TryGetValue(userId, out var user) || user.ReminderMinutes is not { } minutes)
            {
                continue;
            }

            var due = Moment(task.DueDate!.Value, task.DueTime!.Value, user.TimeZone);
            var at = due.AddMinutes(-minutes);
            if (at > now || now - at > Late || task.RemindedFor == due)
            {
                continue;
            }

            task.RemindedFor = due;
            await db.SaveChangesAsync(ct); // first: a failing push service must not get the same reminder every minute
            var badge = await DueCountAsync(db, userId, Dates.Today(user.TimeZone), ct);
            var project = task.Project!.IsInbox ? null : task.Project.Name;
            var time = task.DueTime.Value;
            await push.SendAsync(userId, () => new PushPayload(
                task.Content,
                Body(minutes, time, project),
                $"/Tasks/Edit?id={task.Id}",
                $"task-{task.Id}",
                task.Id,
                badge,
                [new("complete", Text.T("Notify.Complete")), new("tomorrow", Text.T("Notify.Tomorrow"))]), ct);
        }
    }

    private static async Task DigestAsync(AppDbContext db, PushService push, IEnumerable<User> people, DateTime now, CancellationToken ct)
    {
        foreach (var user in people.Where(u => u.DigestTime is not null))
        {
            var local = Dates.Local(now, user.TimeZone);
            var today = DateOnly.FromDateTime(local);
            var clock = TimeOnly.FromDateTime(local);
            if (user.LastDigestDate == today || clock < user.DigestTime!.Value || clock - user.DigestTime.Value > DigestLate)
            {
                continue;
            }

            user.LastDigestDate = today;
            await db.SaveChangesAsync(ct);
            var due = await DueQuery(db, user.Id, today)
                .OrderBy(t => t.DueDate < today).ThenBy(t => t.DueTime == null).ThenBy(t => t.DueTime).ThenBy(t => t.Priority)
                .Select(t => new { t.Content, Overdue = t.DueDate < today }).ToListAsync(ct);
            if (due.Count == 0)
            {
                continue;
            }

            var overdue = due.Count(t => t.Overdue);
            var names = string.Join(", ", due.Take(3).Select(t => t.Content)) + (due.Count > 3 ? " …" : string.Empty);
            await push.SendAsync(user.Id, () => new PushPayload(
                Text.T("Notify.Digest.Title", due.Count),
                (overdue > 0 ? Text.T("Notify.Digest.Overdue", overdue) + " · " : string.Empty) + names,
                "/Today",
                "digest",
                Badge: due.Count), ct);
        }
    }

    /// <summary>Who is reminded of a task: the person it is assigned to, else the owner of a personal project, else the person who created it.</summary>
    public static long? Recipient(TaskItem task)
        => task.AssigneeId ?? (task.Project is { TeamId: null } p ? p.OwnerId : task.CreateUserId ?? task.Project?.OwnerId);

    /// <summary>The moment (UTC) a floating date and time stand for in a zone; a time the clock skips (the change to summer time) is taken an hour later.</summary>
    public static DateTime Moment(DateOnly date, TimeOnly time, string? zone)
    {
        var tz = Dates.Zone(zone);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    /// <summary>"Due at 15:00 · Project" or "In 30 min (15:00) · Project".</summary>
    private static string Body(int minutes, TimeOnly time, string? project)
    {
        var when = minutes == 0 ? Text.T("Notify.DueAt", Dates.Time(time)) : Text.T("Notify.DueIn", minutes, Dates.Time(time));
        return project is null ? when : when + " · " + project;
    }

    /// <summary>The open tasks of a person due today or earlier (the number on the app icon and the daily overview): visible, not given to someone else.</summary>
    private static IQueryable<TaskItem> DueQuery(AppDbContext db, long userId, DateOnly today)
        => db.Tasks.Where(t => !t.IsCompleted && t.DueDate != null && t.DueDate <= today && (t.AssigneeId == null || t.AssigneeId == userId) && !t.Project!.IsArchived
                               && ((t.Project.TeamId == null && t.Project.OwnerId == userId)
                                   || (t.Project.TeamId != null && db.TeamMembers.Any(m => m.TeamId == t.Project.TeamId && m.UserId == userId))));

    public static Task<int> DueCountAsync(AppDbContext db, long userId, DateOnly today, CancellationToken ct = default) => DueQuery(db, userId, today).CountAsync(ct);
}
