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
}

/// <summary>The search, filters and sort of a task list (the toolbar).</summary>
public sealed record TaskQuery(string? Search = null, int? Priority = null, long? LabelId = null, string? Due = null, string Sort = "manual", bool Descending = false);

/// <summary>Reading and changing tasks. Every query starts with the projects the person may see (<see cref="ProjectService.Visible"/>).</summary>
public sealed class TaskService(AppDbContext db, ProjectService projects)
{
    public IQueryable<TaskItem> Visible(long userId)
    {
        var ids = projects.VisibleIds(userId);
        return db.Tasks.Where(t => ids.Contains(t.ProjectId));
    }

    public Task<TaskItem?> FindAsync(long userId, long id)
        => Visible(userId).Include(t => t.Project).Include(t => t.Labels).ThenInclude(l => l.Label).FirstOrDefaultAsync(t => t.Id == id);

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

    /// <summary>Checks a task off. A repeating task moves to its next date instead and stays open. Its sub-tasks are checked off with it.</summary>
    public async Task<(bool Completed, DateOnly? NextDue)> CompleteAsync(long userId, long id, DateOnly today)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return (false, null);
        }

        if (!task.IsCompleted && Recurrence.IsValid(task.Recurrence))
        {
            task.DueDate = Recurrence.Next(task.Recurrence!, task.DueDate ?? today, today);
            await db.SaveChangesAsync();
            return (false, task.DueDate);
        }

        task.IsCompleted = true;
        task.CompletedDate = DateTime.UtcNow;
        foreach (var c in await DescendantsAsync(id, openOnly: true))
        {
            c.IsCompleted = true;
            c.CompletedDate = task.CompletedDate;
        }

        await db.SaveChangesAsync();
        return (true, null);
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

    public async Task<bool> ReopenAsync(long userId, long id)
    {
        var task = await Visible(userId).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return false;
        }

        task.IsCompleted = false;
        task.CompletedDate = null;
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
