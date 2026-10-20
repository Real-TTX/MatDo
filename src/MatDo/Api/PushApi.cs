using System.Text.RegularExpressions;
using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using MatDo.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Api;

/// <summary>
/// The notifications of a device (push.js on the settings page): the public key to subscribe with, subscribe and unsubscribe this browser, and a test.
/// The changes have the guard of <see cref="AppApi"/> (header or anti-forgery token). An endpoint that is not a push service of a browser is refused
/// (<see cref="PushService.IsPushService"/>): the server would post to it.
/// </summary>
public static partial class PushApi
{
    public sealed record SubscribeBody(string? Endpoint, string? P256dh, string? Auth);

    public sealed record EndpointBody(string? Endpoint);

    public static void MapPushApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/app/push").RequireAuthorization();

        group.MapGet("/key", async (PushService push) => Results.Json(new { key = await push.PublicKeyAsync() }));

        var changes = group.MapGroup(string.Empty).AddEndpointFilter(AppApi.GuardAsync);

        changes.MapPost("/subscribe", async (SubscribeBody body, HttpRequest request, CurrentUser me, AppDbContext db) =>
        {
            if (!PushService.IsPushService(body.Endpoint) || body.Endpoint!.Length > 1000 || !IsKey(body.P256dh, 200) || !IsKey(body.Auth, 100))
            {
                return Results.BadRequest();
            }

            // one browser has one subscription: a second account signed in on it takes the device over
            var device = await db.PushDevices.FirstOrDefaultAsync(d => d.Endpoint == body.Endpoint);
            if (device is null)
            {
                device = new PushDevice { Endpoint = body.Endpoint };
                db.PushDevices.Add(device);
            }

            device.UserId = me.RequiredId;
            device.P256dh = body.P256dh!;
            device.Auth = body.Auth!;
            device.Name = DeviceName(request.Headers.UserAgent.ToString());
            device.Language = Languages.Current;
            await db.SaveChangesAsync();
            return Results.Json(new { id = device.Id, name = device.Name });
        });

        changes.MapPost("/unsubscribe", async (EndpointBody body, CurrentUser me, AppDbContext db) =>
        {
            var gone = await db.PushDevices.Where(d => d.UserId == me.RequiredId && d.Endpoint == body.Endpoint).ExecuteDeleteAsync();
            return Results.Json(new { removed = gone });
        });

        changes.MapPost("/test", async (CurrentUser me, AppDbContext db, PushService push) =>
        {
            var badge = await NotificationService.DueCountAsync(db, me.RequiredId, me.Today);
            var sent = await push.SendAsync(me.RequiredId, () => new PushPayload(Text.T("Notify.Test.Title"), Text.T("Notify.Test.Text"), "/Account/Notifications", "test", Badge: badge));
            return Results.Json(new { sent });
        });
    }

    private static bool IsKey(string? value, int max) => value is { Length: > 0 } v && v.Length <= max && KeyText().IsMatch(v);

    /// <summary>"iPhone · Safari", "Android · Chrome", "Windows · Edge" from the user agent, for the list of devices.</summary>
    public static string DeviceName(string agent)
    {
        string system = agent.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : agent.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : agent.Contains("Android", StringComparison.Ordinal) ? "Android"
            : agent.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : agent.Contains("Mac OS", StringComparison.Ordinal) ? "Mac"
            : agent.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : "?";
        string browser = agent.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : agent.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : agent.Contains("SamsungBrowser/", StringComparison.Ordinal) ? "Samsung Internet"
            : agent.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : agent.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "Browser";
        return $"{system} · {browser}";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+={0,2}$")]
    private static partial Regex KeyText();
}
