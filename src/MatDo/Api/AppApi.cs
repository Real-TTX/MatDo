using MatDo.Data;
using MatDo.Services;
using MatDo.Services.Parsing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MatDo.Api;

/// <summary>
/// What the scripts of the pages ask for (signed in, the cookie of the session): the live parse of the editor, creating a task, checking off and reopening, a new date, a
/// priority, delete, and the order after drag and drop. Every page also works without them (forms).
/// <para>
/// A change needs either the anti-forgery token of the page or the header <c>X-MatDo-Request: 1</c>. A page of another site cannot send such a header (it would need CORS,
/// which this app never allows), so the header protects as well as the token does – and the queue of the offline mode (offline.js, sw.js) can send its changes later, also from
/// the service worker, when the page and its token are long gone. A change of the queue also names the account it was made in (<c>X-MatDo-User</c>): when another person is
/// signed in by now, it answers 409 and the queue drops it, so nothing lands in a wrong account. Every change carries the id the device gave it (<c>X-MatDo-Request-Id</c>): sending it twice
/// does it once.
/// </para>
/// </summary>
public static class AppApi
{
    public sealed record ReorderBody(long[] Ids);

    public sealed record DueBody(string? Date);

    public sealed record PriorityBody(int Priority);

    /// <summary>The quick add as JSON: the fields of its form (Pages/Shared/_QuickAdd.cshtml).</summary>
    public sealed record CreateBody(
        string? Text,
        string? Description,
        long? DefaultProjectId,
        long? DefaultSectionId,
        string? DefaultDueDate,
        long? ParentId,
        string? Off,
        string? DueDate,
        string? DueTime,
        bool NoDate,
        string? Priority,
        string? Labels,
        bool ProjectTouched);

    public const string RequestHeader = "X-MatDo-Request";

    public const string UserHeader = "X-MatDo-User";

    /// <summary>The id a device gave a change: a change with an id that was seen is not done again, its first answer comes back (also to a request that arrives while the first one runs).</summary>
    public const string RequestIdHeader = "X-MatDo-Request-Id";

    private static readonly object Gate = new();

    public static void MapAppApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/app").RequireAuthorization();

        group.MapGet("/parse", async (string? text, string? off, CurrentUser me, AppDbContext db, QuickAddService quick) =>
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == me.RequiredId);
            var preview = await quick.PreviewAsync(user, text ?? string.Empty, Off(off));
            var p = preview.Parse;
            var today = Dates.Today(user.TimeZone);
            return Results.Json(new
            {
                title = p.Title,
                tokens = p.Tokens.Select(t => new { kind = t.Kind.ToString().ToLowerInvariant(), start = t.Start, length = t.Length }),
                due = p.DueDate is { } d ? Dates.Iso(d) : null,
                time = p.DueTime is { } t ? Dates.Time(t) : null,
                dueText = p.DueDate is { } dd ? Dates.Relative(dd, p.DueTime, today) : p.DueTime is { } tt ? Dates.Time(tt) : null,
                dueClass = p.DueDate is { } dc ? Ui.Due(dc, p.DueTime, today).Class : null,
                recurrence = p.Recurrence is null ? null : Recurrence.Describe(p.Recurrence),
                project = preview.ProjectName is null ? null : new { id = preview.ProjectId, name = preview.ProjectName, color = preview.ProjectColor, isNew = preview.ProjectIsNew },
                section = preview.SectionName,
                labels = p.Labels,
                priority = p.Priority,
                deadline = p.Deadline is { } dl ? Dates.Iso(dl) : null,
                deadlineText = p.Deadline is { } dlt ? Dates.Relative(dlt, today) : null,
                description = p.Description,
            });
        });

        var changes = group.MapGroup(string.Empty).AddEndpointFilter(GuardAsync);

        changes.MapPost("/tasks", async (CreateBody body, CurrentUser me, AppDbContext db, QuickAddService quick) =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == me.RequiredId);
            var (task, error) = await quick.CreateAsync(user, new QuickAddInput
            {
                Text = body.Text ?? string.Empty,
                Description = body.Description,
                ProjectId = body.ProjectTouched ? body.DefaultProjectId : null,
                DefaultProjectId = body.DefaultProjectId,
                DefaultSectionId = body.DefaultSectionId,
                DefaultDueDate = Dates.ParseIso(body.DefaultDueDate),
                ParentId = body.ParentId,
                DueDate = Dates.ParseIso(body.DueDate),
                DueTime = Dates.ParseTime(body.DueTime),
                NoDate = body.NoDate,
                Priority = int.TryParse(body.Priority, out var p) ? p : null,
                Labels = (body.Labels ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Disabled = Off(body.Off),
            });
            if (task is null)
            {
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            }

            var today = Dates.Today(user.TimeZone);
            var where = task.Project is { IsInbox: true } ? Localization.Text.T("Project.Inbox") : task.Project?.Name ?? string.Empty;
            var answer = new
            {
                id = task.Id,
                content = task.Content,
                message = task.DueDate is { } d
                    ? Localization.Text.T("Tasks.AddedDue", task.Content, where, Dates.Relative(d, task.DueTime, today))
                    : Localization.Text.T("Tasks.Added", task.Content, where),
            };
            return Results.Json(answer, statusCode: StatusCodes.Status201Created);
        });

        changes.MapPost("/tasks/{id:long}/complete", async (long id, CurrentUser me, TaskService tasks) =>
        {
            var (completed, next) = await tasks.CompleteAsync(me.RequiredId, id, me.Today);
            if (!completed && next is null)
            {
                return Results.NotFound();
            }

            return Results.Json(new { completed, nextDue = next is { } n ? Dates.Iso(n) : null, nextDueText = next is { } nt ? Dates.Relative(nt, me.Today) : null });
        });

        changes.MapPost("/tasks/{id:long}/reopen", async (long id, CurrentUser me, TaskService tasks)
            => await tasks.ReopenAsync(me.RequiredId, id) ? Results.Json(new { reopened = true }) : Results.NotFound());

        // the actions of a row (swipe, long press, right click): a new date, a priority, delete
        changes.MapPost("/tasks/{id:long}/due", async (long id, DueBody body, CurrentUser me, TaskService tasks) =>
        {
            DateOnly? due = string.IsNullOrEmpty(body.Date) ? null : Dates.ParseIso(body.Date);
            if (!string.IsNullOrEmpty(body.Date) && due is null)
            {
                return Results.BadRequest();
            }

            return await tasks.SetDueAsync(me.RequiredId, id, due)
                ? Results.Json(new { due = due is { } d ? Dates.Iso(d) : null, dueText = due is { } dt ? Dates.Relative(dt, me.Today) : null })
                : Results.NotFound();
        });

        changes.MapPost("/tasks/{id:long}/priority", async (long id, PriorityBody body, CurrentUser me, TaskService tasks)
            => await tasks.SetPriorityAsync(me.RequiredId, id, body.Priority) ? Results.Json(new { priority = body.Priority }) : Results.NotFound());

        changes.MapPost("/tasks/{id:long}/delete", async (long id, CurrentUser me, TaskService tasks)
            => await tasks.DeleteAsync(me.RequiredId, id) ? Results.Json(new { deleted = true }) : Results.NotFound());

        changes.MapPost("/tasks/reorder", async (ReorderBody body, CurrentUser me, TaskService tasks) =>
        {
            if (body.Ids is null || body.Ids.Length > 2000)
            {
                return Results.BadRequest();
            }

            await tasks.ReorderAsync(me.RequiredId, body.Ids);
            return Results.Json(new { ok = true });
        });
    }

    internal static IReadOnlySet<TokenKind> Off(string? off)
    {
        var set = new HashSet<TokenKind>();
        foreach (var part in (off ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<TokenKind>(part, ignoreCase: true, out var kind))
            {
                set.Add(kind);
                if (kind == TokenKind.Date)
                {
                    set.Add(TokenKind.Time);
                    set.Add(TokenKind.Recurrence);
                }
            }
        }

        return set;
    }

    /// <summary>A change needs the header of the scripts or the anti-forgery token; a change made in another account (the queue of the offline mode) answers 409.</summary>
    internal static async ValueTask<object?> GuardAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.Request.Headers[RequestHeader] != "1")
        {
            try
            {
                await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest();
            }
        }

        var user = http.Request.Headers[UserHeader].ToString();
        var me = http.RequestServices.GetRequiredService<CurrentUser>();
        if (user.Length > 0 && user != me.RequiredId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return Results.Json(new { error = "other account" }, statusCode: StatusCodes.Status409Conflict);
        }

        var requestId = http.Request.Headers[RequestIdHeader].ToString();
        if (requestId.Length is 0 or > 64)
        {
            return await next(context);
        }

        // once per id: the first request runs, every other one (the page and the service worker sending the same queue) waits for its answer
        var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
        var key = $"request:{me.RequiredId}:{requestId}";
        Lazy<Task<object?>> run;
        lock (Gate)
        {
            run = cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(2);
                return new Lazy<Task<object?>>(() => next(context).AsTask());
            })!;
        }

        return await run.Value;
    }
}
