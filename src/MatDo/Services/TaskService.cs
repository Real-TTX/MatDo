using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

public sealed record LabelChip(long Id, string Name, string Color);

/// <summary>One task in a list.</summary>
public sealed record TaskRow(
    long Id,
    string Content,
    string? Description,
    int Priority,
    DateOnly? DueDate,
    TimeOnly? DueTime,
    string? Recurrence,
    DateOnly? Deadline,
    bool IsCompleted,
    DateTime? CompletedDate,
    long ProjectId,
    string ProjectName,
    string ProjectColor,
    bool ProjectIsInbox,
    long? SectionId,
    long? ParentId,
    string? AssigneeName,
    int SortOrder,
    DateTime CreateDate,
    IReadOnlyList<LabelChip> Labels,
    int SubTasks,
    int SubTasksDone,
    int Comments)
{
    /// <summary>The level in a list that shows the sub-tasks under their task (0 = a task of its own; see <see cref="TaskService.Tree"/>).</summary>
    public int Depth { get; init; }

    /// <summary>How many rows of the list stand under this one (its open sub-tasks in the tree).</summary>
    public int Children { get; init; }

    /// <summary>In the list of what was done: the check-off this row stands for (a repeating task has a row for each time).</summary>
    public long? CompletionId { get; init; }

    /// <summary>In the list of what was done: who checked it off.</summary>
    public string? CompletedBy { get; init; }

    /// <summary>The check cannot be taken back from this row (an older check-off of a repeating task).</summary>
    public bool Locked { get; init; }
}

/// <summary>The search, filters and sort of a task list (the toolbar).</summary>
public sealed record TaskQuery(string? Search = null, int? Priority = null, long? LabelId = null, string? Due = null, string Sort = "manual", bool Descending = false);

/// <summary>A check-off on the page of a task: when, for which date, by whom, whose turn it was (when that was someone else), and whether it can be taken back.</summary>
public sealed record CompletionView(long Id, DateTime At, DateOnly? DueDate, string? By, string? Turn, bool Undoable);

/// <summary>A check-off in the list of what was done.</summary>
public sealed record CompletionEntry(long Id, long TaskId, DateTime At, DateOnly? DueDate, string? By, bool Undoable);

/// <summary>One slot of a team's cleaning plan: a repeating task on a day of the week, and whose turn it is. <see cref="IsNext"/> marks the task's real next date (the only one that can be checked off); later rows are a preview of the rotation ahead.</summary>
public sealed record PutzplanEntry(long TaskId, string Content, long ProjectId, string ProjectName, DateOnly Date, long? AssigneeId, bool IsNext, bool Done = false, string? DoneBy = null);

/// <summary>Reading and changing tasks. Every query starts with the projects the person may see (<see cref="ProjectService.Visible"/>).</summary>
public sealed class TaskService(AppDbContext db, ProjectService projects)
{
    public IQueryable<TaskItem> Visible(long userId)
    {
        var ids = projects.VisibleIds(userId);
        return db.Tasks.Where(t => ids.Contains(t.ProjectId));
    }

    public Task<TaskItem?> FindAsync(long userId, long id)
        => Visible(userId).Include(t => t.Project).Include(t => t.Labels).ThenInclude(l => l.Label).Include(t => t.Rotations).FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>The rows of a query, with labels, sub-task and comment counts, filtered and sorted by the toolbar.</summary>
    public async Task<List<TaskRow>> RowsAsync(IQueryable<TaskItem> query, TaskQuery tools, DateOnly today, int limit = 2000)
    {
        if (!string.IsNullOrWhiteSpace(tools.Search))
        {
            var s = tools.Search.Trim().ToLower();
            query = query.Where(t => t.Content.ToLower().Contains(s) || (t.Description != null && t.Description.ToLower().Contains(s)));
        }

        if (tools.Priority is { } p)
        {
            query = query.Where(t => t.Priority == p);
        }

        if (tools.LabelId is { } label)
        {
            query = query.Where(t => t.Labels.Any(l => l.LabelId == label));
        }

        switch (tools.Due)
        {
            case "none":
                query = query.Where(t => t.DueDate == null);
                break;
            case "overdue":
                query = query.Where(t => t.DueDate < today);
                break;
            case "today":
                query = query.Where(t => t.DueDate == today);
                break;
            case "week":
                var end = today.AddDays(7);
                query = query.Where(t => t.DueDate >= today && t.DueDate <= end);
                break;
        }

        var rows = await query
            .Select(t => new
            {
                t.Id, t.Content, t.Description, t.Priority, t.DueDate, t.DueTime, t.Recurrence, t.DeadlineDate, t.IsCompleted, t.CompletedDate,
                t.ProjectId, ProjectName = t.Project!.Name, ProjectColor = t.Project.Color, ProjectIsInbox = t.Project.IsInbox, t.SectionId, t.ParentId,
                Assignee = t.Assignee != null ? t.Assignee.DisplayName : null, t.SortOrder, t.CreateDate,
                Labels = t.Labels.Where(l => l.Label != null && l.Label.UpdateState != SoftEntity.Deleted).Select(l => new { l.Label!.Id, l.Label.Name, l.Label.Color }).ToList(),
                Sub = db.Tasks.Count(c => c.ParentId == t.Id),
                SubDone = db.Tasks.Count(c => c.ParentId == t.Id && c.IsCompleted),
                Comments = db.Comments.Count(c => c.TaskId == t.Id),
            })
            .AsNoTracking()
            .Take(limit)
            .ToListAsync();

        var list = rows.Select(r => new TaskRow(r.Id, r.Content, r.Description, r.Priority, r.DueDate, r.DueTime, r.Recurrence, r.DeadlineDate, r.IsCompleted, r.CompletedDate,
                r.ProjectId, r.ProjectName, r.ProjectColor, r.ProjectIsInbox, r.SectionId, r.ParentId, r.Assignee, r.SortOrder, r.CreateDate,
                r.Labels.Select(l => new LabelChip(l.Id, l.Name, l.Color)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), r.Sub, r.SubDone, r.Comments))
            .ToList();
        return Sort(list, tools.Sort, tools.Descending);
    }

    /// <summary>
    /// The rows as a tree, like Todoist: each task, then its sub-tasks (and theirs) under it, in the order the rows came in; a row whose task is not in the list
    /// (another project, done) stands on its own. Each row gets its <see cref="TaskRow.Depth"/> and the number of rows under it.
    /// </summary>
    public static List<TaskRow> Tree(IReadOnlyList<TaskRow> rows)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        var children = rows.Where(r => r.ParentId is { } p && ids.Contains(p)).GroupBy(r => r.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<TaskRow>(rows.Count);
        void Add(TaskRow row, int depth)
        {
            var below = children.TryGetValue(row.Id, out var list) ? list : [];
            result.Add(row with { Depth = depth, Children = below.Count });
            foreach (var child in below)
            {
                Add(child, depth + 1);
            }
        }

        foreach (var root in rows.Where(r => r.ParentId is not { } p || !ids.Contains(p)))
        {
            Add(root, 0);
        }

        return result;
    }

    public static List<TaskRow> Sort(List<TaskRow> rows, string sort, bool descending)
    {
        IOrderedEnumerable<TaskRow> ordered = sort switch
        {
            "due" => rows.OrderBy(r => r.DueDate is null).ThenBy(r => r.DueDate).ThenBy(r => r.DueTime is null).ThenBy(r => r.DueTime).ThenBy(r => r.Priority),
            "priority" => rows.OrderBy(r => r.Priority).ThenBy(r => r.DueDate is null).ThenBy(r => r.DueDate),
            "name" => rows.OrderBy(r => r.Content, StringComparer.CurrentCultureIgnoreCase),
            "created" => rows.OrderBy(r => r.CreateDate),
            "completed" => rows.OrderBy(r => r.CompletedDate),
            _ => rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Id),
        };
        var list = ordered.ToList();
        if (descending)
        {
            list.Reverse();
        }

        return list;
    }

    /// <summary>
    /// Checks a task off, and notes who did it (<see cref="TaskCompletion"/>). A repeating task moves to its next date instead and stays open: the next person of its
    /// rotation takes it, and its sub-tasks are open again for the next time. A task that is not repeating is completed with its open sub-tasks.
    /// </summary>
    public async Task<(bool Completed, DateOnly? NextDue)> CompleteAsync(long userId, long id, DateOnly today)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return (false, null);
        }

        if (task.IsCompleted)
        {
            return (true, null);
        }

        var now = DateTime.UtcNow;
        if (Recurrence.IsValid(task.Recurrence))
        {
            var done = new TaskCompletion { TaskId = task.Id, UserId = userId, AssigneeId = task.AssigneeId, CompletedAt = now, DueDate = task.DueDate };
            task.DueDate = Recurrence.Next(task.Recurrence!, task.DueDate ?? today, today);
            done.NextDue = task.DueDate;
            await AdvanceRotationAsync(task);
            foreach (var c in (await DescendantsAsync(id)).Where(c => c.IsCompleted))
            {
                c.IsCompleted = false;
                c.CompletedDate = null;
            }

            db.TaskCompletions.Add(done);
            await db.SaveChangesAsync();
            return (false, task.DueDate);
        }

        task.IsCompleted = true;
        task.CompletedDate = now;
        db.TaskCompletions.Add(new TaskCompletion { TaskId = task.Id, UserId = userId, AssigneeId = task.AssigneeId, CompletedAt = now, DueDate = task.DueDate });
        foreach (var c in await DescendantsAsync(id, openOnly: true))
        {
            c.IsCompleted = true;
            c.CompletedDate = now;
            db.TaskCompletions.Add(new TaskCompletion { TaskId = c.Id, UserId = userId, AssigneeId = c.AssigneeId, CompletedAt = now, DueDate = c.DueDate });
        }

        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>
    /// Can the newest check-off of a task be taken back? A completed task: when that check-off completed it. A repeating task: while it still stands on the date that
    /// check-off moved it to (nobody changed it since).
    /// </summary>
    public static bool CanUndo(bool isCompleted, DateOnly? dueDate, DateOnly? nextDue)
        => isCompleted ? nextDue is null : nextDue is not null && dueDate == nextDue;

    /// <summary>The check-offs of a task, newest first, for its page; only the newest can be taken back (<see cref="CanUndo"/>).</summary>
    public async Task<List<CompletionView>> HistoryAsync(TaskItem task, int limit = 50)
    {
        var rows = await db.TaskCompletions.Where(c => c.TaskId == task.Id).OrderByDescending(c => c.CompletedAt).ThenByDescending(c => c.Id).Take(limit)
            .Select(c => new { c.Id, c.CompletedAt, c.DueDate, c.NextDue, c.UserId, By = c.User!.DisplayName, c.AssigneeId, Turn = c.Assignee!.DisplayName })
            .AsNoTracking().ToListAsync();
        return rows.Select((r, i) => new CompletionView(r.Id, r.CompletedAt, r.DueDate, r.By, r.AssigneeId != r.UserId ? r.Turn : null,
            i == 0 && CanUndo(task.IsCompleted, task.DueDate, r.NextDue))).ToList();
    }

    /// <summary>The tasks with at least one check-off (the list of what was done).</summary>
    public IQueryable<TaskItem> Done(long userId) => Visible(userId).Where(t => db.TaskCompletions.Any(c => c.TaskId == t.Id));

    /// <summary>How many check-offs there are in the tasks the person sees.</summary>
    public Task<int> CompletionCountAsync(long userId)
    {
        var ids = Visible(userId).Select(t => t.Id);
        return db.TaskCompletions.CountAsync(c => ids.Contains(c.TaskId));
    }

    /// <summary>The check-offs of these tasks, each marked whether it is the one that can be taken back.</summary>
    public async Task<List<CompletionEntry>> CompletionsAsync(IReadOnlyCollection<long> taskIds)
    {
        var rows = await db.TaskCompletions.Where(c => taskIds.Contains(c.TaskId))
            .Select(c => new { c.Id, c.TaskId, c.CompletedAt, c.DueDate, c.NextDue, By = c.User!.DisplayName, Done = c.Task!.IsCompleted, Due = c.Task.DueDate })
            .AsNoTracking().ToListAsync();
        var newest = rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CompletedAt).ThenByDescending(r => r.Id).First().Id);
        return rows.Select(r => new CompletionEntry(r.Id, r.TaskId, r.CompletedAt, r.DueDate, r.By, newest[r.TaskId] == r.Id && CanUndo(r.Done, r.Due, r.NextDue))).ToList();
    }

    /// <summary>A repeating task with a rotation pool (the "Putzplan") hands over to the next person each time it recurs.</summary>
    private async Task AdvanceRotationAsync(TaskItem task)
    {
        var pool = await db.TaskRotations.Where(r => r.TaskId == task.Id).OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .Select(r => r.UserId).ToListAsync();
        task.AssigneeId = NextAssignee(pool, task.AssigneeId);
    }

    /// <summary>Is there any cleaning plan in the team at all: a repeating team task with a rotation pool (used to tell "nothing this week" from "no plan yet").</summary>
    public Task<bool> HasPutzplanAsync(long userId, long teamId)
        => Visible(userId).AnyAsync(t => !t.IsCompleted && t.Recurrence != null && t.Project!.TeamId == teamId && t.Rotations.Any());

    /// <summary>
    /// The cleaning plan ("Putzplan") of a team for one week: every repeating task with a rotation pool, rolled forward to the instances that fall in the week, each with
    /// the person whose turn it is, and the times it was done that week (from its check-offs, under the person whose turn it was). Only the task's actual next date is
    /// checkable; the rest is a preview of the weeks ahead.
    /// </summary>
    public async Task<List<PutzplanEntry>> PutzplanAsync(long userId, long teamId, DateOnly weekStart, DateOnly weekEnd)
    {
        var items = await Visible(userId)
            .Where(t => !t.IsCompleted && t.Recurrence != null && t.Project!.TeamId == teamId)
            .Include(t => t.Project).Include(t => t.Rotations)
            .ToListAsync();

        var entries = new List<PutzplanEntry>();
        foreach (var t in items)
        {
            if (t.DueDate is not { } start || t.Rotations.Count == 0 || !Recurrence.IsValid(t.Recurrence))
            {
                continue;
            }

            var pool = t.Rotations.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).Select(r => r.UserId).ToList();
            var date = start;
            long? who = t.AssigneeId;
            for (var k = 0; k < 120 && date <= weekEnd; k++)
            {
                if (date >= weekStart)
                {
                    entries.Add(new PutzplanEntry(t.Id, t.Content, t.ProjectId, t.Project!.Name, date, who, date == start));
                }

                var next = Recurrence.Next(t.Recurrence!, date, date);
                if (next <= date)
                {
                    break;
                }

                date = next;
                who = NextAssignee(pool, who);
            }
        }

        var plan = items.Where(t => t.Rotations.Count > 0).ToDictionary(t => t.Id);
        var ids = plan.Keys.ToList();
        var done = await db.TaskCompletions.Where(c => ids.Contains(c.TaskId) && c.DueDate >= weekStart && c.DueDate <= weekEnd)
            .Select(c => new { c.TaskId, c.DueDate, c.AssigneeId, c.UserId, By = c.User!.DisplayName }).AsNoTracking().ToListAsync();
        foreach (var d in done)
        {
            var t = plan[d.TaskId];
            entries.Add(new PutzplanEntry(t.Id, t.Content, t.ProjectId, t.Project!.Name, d.DueDate!.Value, d.AssigneeId ?? d.UserId, false, true, d.By));
        }

        return entries.OrderBy(e => e.Date).ThenBy(e => e.Content, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The next person in a rotation after the current one (wrapping round); the first when the current one is unknown or not in the pool. An empty pool keeps the current person.</summary>
    public static long? NextAssignee(IReadOnlyList<long> pool, long? current)
    {
        if (pool.Count == 0)
        {
            return current;
        }

        var at = -1;
        if (current is { } c)
        {
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i] == c)
                {
                    at = i;
                    break;
                }
            }
        }

        return pool[(at + 1) % pool.Count];
    }

    /// <summary>The sub-tasks of a task, and theirs, to any depth.</summary>
    public async Task<List<TaskItem>> DescendantsAsync(long id, bool openOnly = false)
    {
        var all = new List<TaskItem>();
        var seen = new HashSet<long> { id }; // a loop in the data never runs forever
        var level = new List<long> { id };
        while (level.Count > 0)
        {
            var next = (await db.Tasks.Where(t => t.ParentId != null && level.Contains(t.ParentId.Value) && (!openOnly || !t.IsCompleted)).ToListAsync())
                .Where(t => seen.Add(t.Id)).ToList();
            all.AddRange(next);
            level = next.Select(t => t.Id).ToList();
        }

        return all;
    }

    /// <summary>
    /// Takes the newest check-off back. A completed task is open again (and the check-off leaves the history). A repeating task goes back to the date and the person of
    /// that check-off, as long as nobody changed it since (<see cref="CanUndo"/>); otherwise nothing happens (false).
    /// </summary>
    public async Task<bool> ReopenAsync(long userId, long id)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return false;
        }

        var last = await db.TaskCompletions.Where(c => c.TaskId == id).OrderByDescending(c => c.CompletedAt).ThenByDescending(c => c.Id).FirstOrDefaultAsync();
        if (task.IsCompleted)
        {
            task.IsCompleted = false;
            task.CompletedDate = null;
            if (last is { NextDue: null })
            {
                db.TaskCompletions.Remove(last);
            }

            await db.SaveChangesAsync();
            return true;
        }

        if (last is null || !CanUndo(false, task.DueDate, last.NextDue))
        {
            return false;
        }

        task.DueDate = last.DueDate;
        task.AssigneeId = last.AssigneeId;
        db.TaskCompletions.Remove(last);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>A new due date from the menu of a row (today, tomorrow ...); none removes the time and the repetition too. The time of day stays.</summary>
    public async Task<bool> SetDueAsync(long userId, long id, DateOnly? due)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return false;
        }

        task.DueDate = due;
        if (due is null)
        {
            task.DueTime = null;
            task.Recurrence = null;
            task.RecurrenceText = null;
        }

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetPriorityAsync(long userId, long id, int priority)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null || priority is < 1 or > 4)
        {
            return false;
        }

        task.Priority = priority;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(long userId, long id)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return false;
        }

        db.Tasks.RemoveRange(await DescendantsAsync(id, openOnly: false));
        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>The order of the tasks of a list after drag and drop: the ids in their new order (only the visible ones are touched).</summary>
    public async Task ReorderAsync(long userId, IReadOnlyList<long> ids)
    {
        var tasks = await Visible(userId).Where(t => ids.Contains(t.Id)).ToListAsync();
        var byId = tasks.ToDictionary(t => t.Id);
        for (var i = 0; i < ids.Count; i++)
        {
            if (byId.TryGetValue(ids[i], out var t))
            {
                t.SortOrder = i + 1;
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The labels of a person (filters, pickers).</summary>
    public async Task<List<LabelChip>> LabelChipsAsync(long userId)
        => (await db.Labels.Where(l => l.OwnerId == userId).Select(l => new { l.Id, l.Name, l.Color }).AsNoTracking().ToListAsync())
            .Select(l => new LabelChip(l.Id, l.Name, l.Color)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<int> NextSortOrderAsync(long projectId)
        => (await db.Tasks.Where(t => t.ProjectId == projectId).MaxAsync(t => (int?)t.SortOrder) ?? 0) + 1;

    /// <summary>Gives the task exactly these labels (of the person; unknown names are created).</summary>
    public async Task SetLabelsAsync(TaskItem task, long userId, IEnumerable<string> names, bool createMissing = true)
    {
        var wanted = names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var labels = await LabelsByNameAsync(userId, wanted, createMissing);
        var current = await db.TaskLabels.Where(l => l.TaskId == task.Id).ToListAsync();
        db.TaskLabels.RemoveRange(current.Where(c => labels.All(l => l.Id != c.LabelId)));
        foreach (var l in labels.Where(l => current.All(c => c.LabelId != l.Id)))
        {
            db.TaskLabels.Add(new TaskLabel { TaskId = task.Id, LabelId = l.Id });
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<Label>> LabelsByNameAsync(long userId, IReadOnlyList<string> names, bool createMissing)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var lower = names.Select(n => n.ToLower()).ToList();
        var found = await db.Labels.Where(l => l.OwnerId == userId && lower.Contains(l.Name.ToLower())).ToListAsync();
        if (createMissing)
        {
            foreach (var name in names.Where(n => found.All(f => !string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))))
            {
                var label = new Label { OwnerId = userId, Name = name.Length > 60 ? name[..60] : name, Color = "#808080" };
                db.Labels.Add(label);
                found.Add(label);
            }

            await db.SaveChangesAsync();
        }

        return found;
    }
}
