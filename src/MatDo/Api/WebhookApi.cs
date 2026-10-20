using System.Text.Json;
using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Api;

/// <summary>
/// The webhook: <c>POST /api/v1/webhook/{token}</c> creates a task from a text, exactly like the quick add (dates, times, #project, @label, p1 and the keywords of the person).
/// Made for Apple Shortcuts ("Dictate text" → "Get contents of URL"), Zapier, n8n or curl. The body is JSON (<c>{"text": "...", "description": "..."}</c>), a form
/// (<c>text=...</c>) or plain text. The token can also be sent as <c>Authorization: Bearer &lt;token&gt;</c> to <c>POST /api/v1/webhook</c>.
/// An unknown or switched-off token answers 404, a text without a title 400, more than 60 calls a minute 429.
/// </summary>
public static class WebhookApi
{
    public sealed record WebhookBody(string? Text, string? Description, string? Content);

    public static void MapWebhookApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").AllowAnonymous().DisableAntiforgery().RequireRateLimiting("webhook");
        group.MapPost("/webhook/{token}", (string token, HttpContext http) => HandleAsync(token, http));
        group.MapPost("/webhook", (Delegate)((HttpContext http) =>
        {
            var header = http.Request.Headers.Authorization.ToString();
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : string.Empty;
            return HandleAsync(token, http);
        }));
    }

    private static async Task<IResult> HandleAsync(string token, HttpContext http)
    {
        if (!Guid.TryParse(token, out var guid))
        {
            return NotFound();
        }

        var services = http.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Token == guid && w.IsActive);
        var user = hook is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == hook.UserId && u.IsActive);
        if (hook is null || user is null)
        {
            return NotFound();
        }

        // the answer (message, dates) is in the language of the person the token belongs to; without one, the language of the calling device
        var language = Languages.IsSupported(user.Language) ? user.Language : http.Request.Headers.AcceptLanguage.ToString();
        System.Globalization.CultureInfo.CurrentUICulture = Languages.UiCulture(language);

        var (text, description) = await ReadAsync(http.Request);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Results.Json(new { error = Text.T("Webhook.TextMissing") }, statusCode: StatusCodes.Status400BadRequest);
        }

        services.GetRequiredService<CurrentUser>().ActingUserId = user.Id;
        var quick = services.GetRequiredService<QuickAddService>();
        var (task, error) = await quick.CreateAsync(user, new QuickAddInput
        {
            Text = text.Length > 2000 ? text[..2000] : text,
            Description = description,
            DefaultProjectId = hook.ProjectId,
            Source = TaskSource.Webhook,
        });
        if (task is null)
        {
            return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
        }

        hook.LastUsedDate = DateTime.UtcNow;
        hook.UseCount++;
        await db.SaveChangesAsync();

        // the open windows show it at once (the inbox, the project)
        await services.GetRequiredService<LiveService>().TasksChangedAsync(task.ProjectId, new LiveEvent("tasks", Task: task.Id, Title: task.Content, Source: "webhook"));

        var labels = await db.TaskLabels.Where(l => l.TaskId == task.Id && l.Label != null).Select(l => l.Label!.Name).ToListAsync();
        var today = Dates.Today(user.TimeZone);
        return Results.Json(new
        {
            id = task.Id,
            content = task.Content,
            description = task.Description,
            project = ProjectName(task),
            due = task.DueDate is { } d ? Dates.Iso(d) : null,
            time = task.DueTime is { } t ? Dates.Time(t) : null,
            dueText = task.DueDate is { } dd ? Dates.Relative(dd, task.DueTime, today) : null,
            recurrence = task.Recurrence is null ? null : Recurrence.Describe(task.Recurrence),
            deadline = task.DeadlineDate is { } dl ? Dates.Iso(dl) : null,
            priority = task.Priority,
            labels,
            url = $"/Tasks/Edit?id={task.Id}",
            message = Summary(task, labels, today),
        }, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>One sentence for the notification of the shortcut: "„Milch kaufen“ – Morgen 17:00 – Einkauf".</summary>
    private static string Summary(TaskItem task, IReadOnlyList<string> labels, DateOnly today)
    {
        var parts = new List<string> { Text.T("Webhook.Quoted", task.Content) };
        if (task.DueDate is { } d)
        {
            parts.Add(Dates.Relative(d, task.DueTime, today));
        }

        parts.Add(ProjectName(task));
        if (task.Priority < 4)
        {
            parts.Add($"P{task.Priority}");
        }

        parts.AddRange(labels.Select(l => "@" + l));
        return string.Join(" – ", parts);
    }

    private static async Task<(string? Text, string? Description)> ReadAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            return (Value(form["text"]) ?? Value(form["content"]), Value(form["description"]));
        }

        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        if (body.Length > 20000)
        {
            body = body[..20000];
        }

        var type = request.ContentType ?? string.Empty;
        var trimmed = body.TrimStart();
        if (type.Contains("json", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith('{'))
        {
            try
            {
                var json = JsonSerializer.Deserialize<WebhookBody>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return (json?.Text ?? json?.Content, json?.Description);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }

        return (body.Trim(), null);
    }

    private static string? Value(Microsoft.Extensions.Primitives.StringValues v) => string.IsNullOrWhiteSpace(v.ToString()) ? null : v.ToString();

    private static string ProjectName(TaskItem task) => task.Project is null or { IsInbox: true } ? Text.T("Project.Inbox") : task.Project.Name;

    private static IResult NotFound() => Results.Json(new { error = Text.T("Webhook.Unknown") }, statusCode: StatusCodes.Status404NotFound);
}
