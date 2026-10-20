using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using MatDo.Data;
using MatDo.Localization;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services.Notifications;

/// <summary>A button of a notification: what the service worker does (complete, tomorrow) and its text.</summary>
public sealed record PushAction(string Action, string Title);

/// <summary>What a notification shows and what a click does; the service worker (sw.js, "push") draws it.</summary>
public sealed record PushPayload(string Title, string Body, string Url, string Tag, long? Task = null, int? Badge = null, IReadOnlyList<PushAction>? Actions = null)
{
    /// <summary>The account the notification belongs to: the actions of the service worker send it, so a change never lands in another account.</summary>
    public long User { get; init; }
}

/// <summary>
/// Sends notifications (Web Push, RFC 8030/8291/8292) to the devices of a person. The key pair of the app (VAPID) is made at the first need and kept in
/// <see cref="SystemSetting"/>; a subscription the push service no longer knows (404, 410) is deleted. Only the push services of the browsers are called:
/// an endpoint elsewhere is refused when a device subscribes (<see cref="IsPushService"/>), so the server never posts to an address a user chose.
/// </summary>
public sealed class PushService(AppDbContext db, IHttpClientFactory http, ILogger<PushService> logger)
{
    private static readonly SemaphoreSlim KeyGate = new(1, 1);

    private static readonly string[] PushHosts = ["fcm.googleapis.com", "android.googleapis.com", "updates.push.services.mozilla.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>An endpoint of a push service of a browser: https, on a host of Google, Mozilla, Apple or Microsoft (or below it).</summary>
    public static bool IsPushService(string? endpoint)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
           && PushHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    /// <summary>The public key the browsers subscribe with (base64url of the uncompressed point).</summary>
    public async Task<string> PublicKeyAsync(CancellationToken ct = default) => (await KeysAsync(ct)).Public;

    /// <summary>Sends to every device of a person; the text is made per device, in the language of the person (or of the device when the person chose "automatic"). Returns the number delivered.</summary>
    public async Task<int> SendAsync(long userId, Func<PushPayload> build, CancellationToken ct = default)
    {
        var devices = await db.PushDevices.Where(d => d.UserId == userId).ToListAsync(ct);
        if (devices.Count == 0)
        {
            return 0;
        }

        var language = await db.Users.Where(u => u.Id == userId).Select(u => u.Language).FirstOrDefaultAsync(ct);
        var (publicKey, privateKey, subject) = await KeysAsync(ct);
        var client = new PushServiceClient(http.CreateClient("push"))
        {
            DefaultAuthentication = new VapidAuthentication(publicKey, privateKey) { Subject = subject },
        };

        var sent = 0;
        foreach (var device in devices)
        {
            var code = Languages.IsSupported(language) ? language! : Languages.IsSupported(device.Language) ? device.Language : Languages.Default;
            var payload = Languages.In(code, build) with { User = userId };
            var message = new PushMessage(JsonSerializer.Serialize(payload, Json)) { Urgency = PushMessageUrgency.High, TimeToLive = 6 * 3600 };
            var subscription = new PushSubscription { Endpoint = device.Endpoint, Keys = new Dictionary<string, string> { ["p256dh"] = device.P256dh, ["auth"] = device.Auth } };
            try
            {
                await client.RequestPushMessageDeliveryAsync(subscription, message, ct);
                device.LastSentDate = DateTime.UtcNow;
                sent++;
            }
            catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                db.PushDevices.Remove(device); // the browser ended the subscription (signed out of the browser, app removed, permission taken back)
            }
            catch (Exception ex) when (ex is PushServiceClientException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A notification to device {Device} failed.", device.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>The key pair and the contact of the app (VAPID needs one: the address of the first administrator).</summary>
    private async Task<(string Public, string Private, string Subject)> KeysAsync(CancellationToken ct)
    {
        await KeyGate.WaitAsync(ct);
        try
        {
            var settings = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
            if (settings is null)
            {
                settings = new SystemSetting { Id = 1 };
                db.SystemSettings.Add(settings);
            }

            if (string.IsNullOrEmpty(settings.VapidPublicKey) || string.IsNullOrEmpty(settings.VapidPrivateKey))
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var p = key.ExportParameters(includePrivateParameters: true);
                settings.VapidPublicKey = Base64Url([0x04, .. p.Q.X!, .. p.Q.Y!]);
                settings.VapidPrivateKey = Base64Url(p.D!);
                await db.SaveChangesAsync(ct);
            }

            var admin = await db.Users.Where(u => u.Role == UserRole.Admin).OrderBy(u => u.Id).Select(u => u.Email).FirstOrDefaultAsync(ct);
            var subject = !string.IsNullOrWhiteSpace(settings.PublicUrl) && settings.PublicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? settings.PublicUrl.TrimEnd('/')
                : "mailto:" + (admin ?? "admin@matdo.invalid");
            return (settings.VapidPublicKey, settings.VapidPrivateKey!, subject);
        }
        finally
        {
            KeyGate.Release();
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
