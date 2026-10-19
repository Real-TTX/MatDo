using MatDo.Data;
using MatDo.Services;
using MatDo.Services.Parsing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Api;

/// <summary>
/// What the scripts of the pages ask for (signed in, the cookie of the session; a change also needs the anti-forgery token of the page in the header <c>RequestVerificationToken</c>):
/// the live parse of the editor, checking off and reopening a task, and the order after drag and drop. Every page also works without them (forms).
/// </summary>
public static class AppApi
{
    public sealed record ReorderBody(long[] Ids);

    public sealed record DueBody(string? Date);

    public sealed record PriorityBody(int Priority);

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

        group.MapPost("/tasks/{id:long}/complete", async (long id, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti))
            {
                return Results.BadRequest();
            }

            var (completed, next) = await tasks.CompleteAsync(me.RequiredId, id, me.Today);
            if (!completed && next is null)
            {
                return Results.NotFound();
            }

            return Results.Json(new { completed, nextDue = next is { } n ? Dates.Iso(n) : null, nextDueText = next is { } nt ? Dates.Relative(nt, me.Today) : null });
        });

        group.MapPost("/tasks/{id:long}/reopen", async (long id, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti))
            {
                return Results.BadRequest();
            }

            return await tasks.ReopenAsync(me.RequiredId, id) ? Results.Json(new { reopened = true }) : Results.NotFound();
        });

        // the actions of a row (swipe, long press, right click): a new date, a priority, delete
        group.MapPost("/tasks/{id:long}/due", async (long id, DueBody body, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti))
            {
                return Results.BadRequest();
            }

            DateOnly? due = string.IsNullOrEmpty(body.Date) ? null : Dates.ParseIso(body.Date);
            if (!string.IsNullOrEmpty(body.Date) && due is null)
            {
                return Results.BadRequest();
            }

            return await tasks.SetDueAsync(me.RequiredId, id, due)
                ? Results.Json(new { due = due is { } d ? Dates.Iso(d) : null, dueText = due is { } dt ? Dates.Relative(dt, me.Today) : null })
                : Results.NotFound();
        });

        group.MapPost("/tasks/{id:long}/priority", async (long id, PriorityBody body, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti))
            {
                return Results.BadRequest();
            }

            return await tasks.SetPriorityAsync(me.RequiredId, id, body.Priority) ? Results.Json(new { priority = body.Priority }) : Results.NotFound();
        });

        group.MapPost("/tasks/{id:long}/delete", async (long id, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti))
            {
                return Results.BadRequest();
            }

            return await tasks.DeleteAsync(me.RequiredId, id) ? Results.Json(new { deleted = true }) : Results.NotFound();
        });

        group.MapPost("/tasks/reorder", async (ReorderBody body, HttpContext http, CurrentUser me, TaskService tasks, IAntiforgery anti) =>
        {
            if (!await Valid(http, anti) || body.Ids is null || body.Ids.Length > 2000)
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

    private static async Task<bool> Valid(HttpContext http, IAntiforgery anti)
    {
        try
        {
            await anti.ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
