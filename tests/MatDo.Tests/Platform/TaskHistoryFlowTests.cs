using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatDo.Tests.Platform;

/// <summary>
/// The history of check-offs on a real database: a repeating task with a rotation notes who did it, hands over, opens its sub-tasks again and can be taken back;
/// a task completed for good is reopened without its check-off.
/// </summary>
public sealed class TaskHistoryFlowTests(MatDoFactory site) : IClassFixture<MatDoFactory>
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    [DbFact]
    public async Task A_repeating_check_off_is_noted_hands_over_and_can_be_taken_back()
    {
        var (anna, kim, project) = await TeamAsync();
        var (task, sub) = await WithAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var t = new TaskItem { ProjectId = project, Content = "Küche putzen", DueDate = Today, Recurrence = "w:1", AssigneeId = anna };
            db.Tasks.Add(t);
            await db.SaveChangesAsync();
            var c = new TaskItem { ProjectId = project, ParentId = t.Id, Content = "Herd", IsCompleted = true, CompletedDate = DateTime.UtcNow };
            db.Tasks.Add(c);
            db.TaskRotations.AddRange(new TaskRotation { TaskId = t.Id, UserId = anna, SortOrder = 0 }, new TaskRotation { TaskId = t.Id, UserId = kim, SortOrder = 1 });
            await db.SaveChangesAsync();
            return (t.Id, c.Id);
        });

        // Kim checks it off although it was Anna's turn.
        var (completed, next) = await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(kim, task, Today));
        Assert.False(completed);
        Assert.Equal(Today.AddDays(7), next);

        var after = await TaskAsync(task);
        Assert.Equal(kim, after.AssigneeId);
        Assert.False((await TaskAsync(sub)).IsCompleted);
        var done = await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == task));
        Assert.Equal(kim, done.UserId);
        Assert.Equal(anna, done.AssigneeId);
        Assert.Equal(Today, done.DueDate);
        Assert.Equal(Today.AddDays(7), done.NextDue);

        var history = await WithAsync(async s => await s.GetRequiredService<TaskService>().HistoryAsync(await s.GetRequiredService<AppDbContext>().Tasks.SingleAsync(t => t.Id == task)));
        var entry = Assert.Single(history);
        Assert.True(entry.Undoable);
        Assert.Equal("Anna", entry.Turn);

        // Taken back: the date and the person of that check-off, and no trace of it.
        Assert.True(await WithAsync(s => s.GetRequiredService<TaskService>().ReopenAsync(anna, task)));
        var back = await TaskAsync(task);
        Assert.Equal(Today, back.DueDate);
        Assert.Equal(anna, back.AssigneeId);
        Assert.False(await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.AnyAsync(c => c.TaskId == task)));

        // Moved by hand after a check-off: that check-off stays.
        await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, Today));
        await WithAsync(s => s.GetRequiredService<TaskService>().SetDueAsync(anna, task, Today.AddDays(9)));
        Assert.False(await WithAsync(s => s.GetRequiredService<TaskService>().ReopenAsync(anna, task)));
        Assert.Equal(1, await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.CountAsync(c => c.TaskId == task)));
    }

    [DbFact]
    public async Task A_completed_task_is_noted_and_reopening_takes_the_check_off_back()
    {
        var (anna, _, project) = await TeamAsync();
        var task = await WithAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var t = new TaskItem { ProjectId = project, Content = "Fenster", DueDate = Today };
            db.Tasks.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });

        var (completed, _) = await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, Today));
        Assert.True(completed);
        // a second check-off of a completed task notes nothing more
        await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, Today));
        var done = await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.AsNoTracking().SingleAsync(c => c.TaskId == task));
        Assert.Null(done.NextDue);
        Assert.Equal(anna, done.UserId);

        Assert.True(await WithAsync(s => s.GetRequiredService<TaskService>().ReopenAsync(anna, task)));
        Assert.False((await TaskAsync(task)).IsCompleted);
        Assert.False(await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.AnyAsync(c => c.TaskId == task)));
    }

    [DbFact]
    public async Task A_repeat_counts_from_the_check_off_moves_its_deadline_and_ends_on_its_end_date()
    {
        var (anna, _, project) = await TeamAsync();
        var task = await WithAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            // due ten days ago, every 3 months from the check-off, the deadline two days after the date, until the end of February
            var t = new TaskItem { ProjectId = project, Content = "Filter wechseln", DueDate = Today.AddDays(-10), DeadlineDate = Today.AddDays(-8), Recurrence = "m:3;c;u=2027-02-28" };
            db.Tasks.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });

        var (completed, next) = await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, Today));
        Assert.False(completed);
        Assert.Equal(new DateOnly(2027, 2, 20), next);
        Assert.Equal(new DateOnly(2027, 2, 22), (await TaskAsync(task)).DeadlineDate);

        // taken back: the date and the deadline as they were
        Assert.True(await WithAsync(s => s.GetRequiredService<TaskService>().ReopenAsync(anna, task)));
        var back = await TaskAsync(task);
        Assert.Equal(Today.AddDays(-10), back.DueDate);
        Assert.Equal(Today.AddDays(-8), back.DeadlineDate);

        // checked off on 2027-02-20 the next time would be in May, after the end: done for good, with the check-off noted
        await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, Today));
        (completed, next) = await WithAsync(s => s.GetRequiredService<TaskService>().CompleteAsync(anna, task, new DateOnly(2027, 2, 20)));
        Assert.True(completed);
        Assert.Null(next);
        var last = await TaskAsync(task);
        Assert.True(last.IsCompleted);
        Assert.Equal(new DateOnly(2027, 2, 20), last.DueDate);
        Assert.Equal(2, await WithAsync(s => s.GetRequiredService<AppDbContext>().TaskCompletions.CountAsync(c => c.TaskId == task)));
    }

    /// <summary>Two people (Anna, Kim) in a team with one project.</summary>
    private async Task<(long Anna, long Kim, long Project)> TeamAsync()
    {
        var anna = await UserAsync("Anna");
        var kim = await UserAsync("Kim");
        var project = await WithAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var team = new Team { Name = "WG " + Guid.NewGuid().ToString("N")[..6] };
            team.Members.Add(new TeamMember { UserId = anna, Role = TeamRole.Owner });
            team.Members.Add(new TeamMember { UserId = kim, Role = TeamRole.Member });
            db.Teams.Add(team);
            await db.SaveChangesAsync();
            var p = new Project { OwnerId = anna, TeamId = team.Id, Name = "Küche" };
            db.Projects.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });
        return (anna, kim, project);
    }

    private Task<long> UserAsync(string name) => WithAsync(async s =>
    {
        var email = "u" + Guid.NewGuid().ToString("N")[..10] + "@example.com";
        var (user, error) = await s.GetRequiredService<AccountService>().RegisterAsync(email, name, "Correct-Horse-Battery");
        Assert.True(user is not null, error?.Message);
        return user!.Id;
    });

    private Task<TaskItem> TaskAsync(long id) => WithAsync(s => s.GetRequiredService<AppDbContext>().Tasks.AsNoTracking().SingleAsync(t => t.Id == id));

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = site.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task WithAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = site.Services.CreateScope();
        await work(scope.ServiceProvider);
    }
}
