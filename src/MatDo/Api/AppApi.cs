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
/// which this app never allows), so the header protects as well as the token does – and the notes made without a network (offline.js, <c>POST /api/app/capture</c>) can be
/// sent later from another page. A request may name the account it was made in (<c>X-MatDo-User</c>; another one signed in by now answers 409) and carry an id
/// (<c>X-MatDo-Request-Id</c>): sending it twice does it once. <c>X-MatDo-Tab</c> names the window, which is not told about its own change (LiveService).
/// </para>
/// </summary>
public static class AppApi
{
    public sealed record ReorderBody(long[] Ids);

    public sealed record DueBody(string? Date);

    public sealed record PriorityBody(int Priority);

    public sealed record CaptureBody(string? Text);

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

        // the open windows of the person (live.js): a stream of Server-Sent Events, a comment every 25 s keeps proxies from closing it
        group.MapGet("/events", async (HttpContext http, CurrentUser me, LiveService live, string? tab) =>
        {
            var ct = http.RequestAborted;
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache, no-transform";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
            var (id, reader) = live.Open(me.RequiredId, tab is { Length: <= 64 } ? tab : string.Empty);
            try
            {
                await http.Response.WriteAsync("retry: 5000\n: open\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
                while (!ct.IsCancellationRequested)
                {
                    var wait = reader.WaitToReadAsync(ct).AsTask();
                    if (await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(25), ct)) == wait)
                    {
                        if (!await wait)
                        {
                            break;
                        }

                        while (reader.TryRead(out var message))
                        {
                            await http.Response.WriteAsync(message, ct);
                        }
                    }
                    else
                    {
                        await http.Response.WriteAsync(": ping\n\n", ct);
                    }

                    await http.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // the window went away
            }
            finally
            {
                live.Close(me.RequiredId, id);
            }
        });

        var changes = group.MapGroup(string.Empty).AddEndpointFilter(GuardAsync).AddEndpointFilter(LiveAsync);

        changes.MapPost("/tasks", async (CreateBody body, HttpContext http, CurrentUser me, AppDbContext db, QuickAddService quick) =>
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

            http.Items[LiveTaskKey] = task.Id;
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

        // a note made without a network (offline.js): read like the webhook (keywords, #project), into the inbox
        changes.MapPost("/capture", async (CaptureBody body, IServiceProvider services, CurrentUser me, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
            {
                return Results.BadRequest();
            }

            var user = await db.Users.FirstAsync(u => u.Id == me.RequiredId);
            var (task, error) = await WebhookApi.FromTextAsync(services, user, body.Text.Trim(), null, null, TaskSource.Offline);
            return task is null ? Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest) : Results.Json(new { id = task.Id, content = task.Content }, statusCode: StatusCodes.Status201Created);
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

        changes.MapPost("/tasks/reorder", async (ReorderBody body, HttpContext http, CurrentUser me, TaskService tasks) =>
        {
            if (body.Ids is null || body.Ids.Length > 2000)
            {
                return Results.BadRequest();
            }

            if (body.Ids.Length > 0)
            {
                http.Items[LiveTaskKey] = body.Ids[0];
            }

            await tasks.ReorderAsync(me.RequiredId, body.Ids);
            return Results.Json(new { ok = true });
        });
    }

    private const string LiveTaskKey = "MatDo.LiveTask";

    /// <summary>After a change that went through, the other open windows of the people who see the project reload their lists (LiveService); the window that made it is not told.</summary>
    private static async ValueTask<object?> LiveAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (result is IStatusCodeHttpResult { StatusCode: >= 300 })
        {
            return result;
        }

        var http = context.HttpContext;
        long? taskId = http.Items[LiveTaskKey] as long?
            ?? (http.Request.RouteValues.TryGetValue("id", out var v) && long.TryParse(v?.ToString(), out var n) ? n : null);
        if (taskId is { } id)
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var task = await db.Tasks.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == id).Select(t => new { t.ProjectId, t.Content }).FirstOrDefaultAsync();
            if (task is not null)
            {
                await http.RequestServices.GetRequiredService<LiveService>()
                    .TasksChangedAsync(task.ProjectId, new LiveEvent("tasks", Task: id, Title: task.Content), http.Request.Headers[LiveService.TabHeader].ToString());
            }
        }

        return result;
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
