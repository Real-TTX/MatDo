using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatDo.Tests.Platform;

/// <summary>The platform contract of MatDesign docs/PLATFORM.md as MatDo answers it: shapes, status codes, access (the whole app, PostgreSQL).</summary>
public sealed class PlatformApiTests(MatDoFactory site) : IClassFixture<MatDoFactory>
{
    private static readonly string[] Protected = ["/api/state", "/api/state/openTasks", "/api/configure"];

    [DbFact]
    public async Task Health_answers_without_a_key()
    {
        var response = await site.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("matdo", doc.RootElement.GetProperty("app").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("version").GetString()));
        Assert.Contains(doc.RootElement.GetProperty("checks").EnumerateArray(),
            c => c.GetProperty("name").GetString() == "database" && c.GetProperty("status").GetString() == "ok");

        // The old address stays for whoever watches it.
        Assert.Equal(HttpStatusCode.OK, (await site.CreateClient().GetAsync("/health")).StatusCode);
    }

    [DbFact]
    public async Task The_served_definition_is_valid_has_an_etag_and_no_secret()
    {
        var client = site.CreateClient();
        var response = await client.GetAsync("/api");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Empty(MatPlatform.MatPlatformValidator.Validate(text, MatPlatform.MatPlatformValidator.ReadSprite(Repo.Sprite)));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api");
        request.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(request)).StatusCode);

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("contract").GetInt32());
        Assert.Equal("matdo", root.GetProperty("app").GetProperty("id").GetString());
        Assert.Equal("#8300bc", root.GetProperty("app").GetProperty("accent").GetString());
        Assert.True(root.GetProperty("auth").GetProperty("apiKey").GetBoolean());
        Assert.False(root.GetProperty("auth").TryGetProperty("identity", out _));

        // Never the key, a user, the database or a path of the host.
        Assert.DoesNotContain(MatDoFactory.ApiKey, text);
        Assert.DoesNotContain(MatDoFactory.AdminEmail, text);
        Assert.DoesNotContain("matdo_t_", text);
    }

    [DbFact]
    public async Task The_definition_follows_accept_language()
    {
        var client = site.CreateClient();
        using var german = new HttpRequestMessage(HttpMethod.Get, "/api");
        german.Headers.TryAddWithoutValidation("Accept-Language", "de-DE");
        using var english = new HttpRequestMessage(HttpMethod.Get, "/api");
        english.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

        using var de = JsonDocument.Parse(await (await client.SendAsync(german)).Content.ReadAsStringAsync());
        using var en = JsonDocument.Parse(await (await client.SendAsync(english)).Content.ReadAsStringAsync());
        Assert.Equal("Übersicht", de.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
        Assert.Equal("Overview", en.RootElement.GetProperty("pages")[0].GetProperty("label").GetString());
    }

    [DbFact]
    public async Task State_configure_and_actions_need_the_key()
    {
        var anonymous = site.CreateClient();
        foreach (var path in Protected)
        {
            var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = false } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/actions/cleanUp", null)).StatusCode);

        var wrong = site.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync("/api/state")).StatusCode);

        // A bearer token is no way in while MatIdentity is not configured.
        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "abc.def.ghi");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
    }

    [DbFact]
    public async Task State_answers_every_tile_with_the_real_numbers_and_one_value_by_key()
    {
        await AddSampleDataAsync();

        var api = site.Api();
        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        var text = await api.GetStringAsync("/api/state");
        using var state = JsonDocument.Parse(text);
        var values = state.RootElement.GetProperty("values");
        Assert.True(state.RootElement.TryGetProperty("time", out _));

        foreach (var key in definition.RootElement.GetProperty("state").EnumerateArray().Select(s => s.GetProperty("key").GetString()!))
        {
            Assert.True(values.TryGetProperty(key, out var value), $"No value for {key}");
            Assert.True(value.TryGetProperty("value", out _), $"{key} has no value field");
        }

        long Number(string key) => values.GetProperty(key).GetProperty("value").GetInt64();

        // The administrator and one more active person; one team; the three inboxes and a project (an archived one does not count);
        // three open tasks (one overdue), one done yesterday, one done long ago, one deleted; two webhooks of which one is on; one device.
        Assert.Equal(2, Number("users"));
        Assert.Equal(1, Number("teams"));
        Assert.Equal(4, Number("projects"));
        Assert.Equal(3, Number("openTasks"));
        Assert.Equal(1, Number("overdueTasks"));
        Assert.Equal("warn", values.GetProperty("overdueTasks").GetProperty("level").GetString());
        Assert.Equal(1, Number("doneLast7Days"));
        Assert.Equal(1, Number("webhooks"));
        Assert.Equal(1, Number("pushDevices"));
        Assert.Equal("info", values.GetProperty("registration").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.String, values.GetProperty("lastActivity").GetProperty("value").ValueKind);

        // No secret of the sample data: no key of a device, no token of a webhook.
        Assert.DoesNotContain("device-secret", text);

        var one = await api.GetAsync("/api/state/openTasks");
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        using (var doc = JsonDocument.Parse(await one.Content.ReadAsStringAsync()))
        {
            Assert.Equal("openTasks", doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("value").GetInt32());
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/state/nothing")).StatusCode);
    }

    [DbFact]
    public async Task Configure_answers_every_field_and_saves_a_change_where_the_admin_page_reads_it()
    {
        var api = site.Api();
        using var definition = JsonDocument.Parse(await api.GetStringAsync("/api"));
        using (var configuration = JsonDocument.Parse(await api.GetStringAsync("/api/configure")))
        {
            var values = configuration.RootElement.GetProperty("values");
            foreach (var field in definition.RootElement.GetProperty("configure").EnumerateArray())
            {
                Assert.True(values.TryGetProperty(field.GetProperty("key").GetString()!, out _));
            }
        }

        var saved = await api.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = false, publicUrl = "https://todo.example.com/" } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using (var doc = JsonDocument.Parse(await saved.Content.ReadAsStringAsync()))
        {
            var values = doc.RootElement.GetProperty("values");
            Assert.False(values.GetProperty("registrationOpen").GetBoolean());
            Assert.Equal("https://todo.example.com", values.GetProperty("publicUrl").GetString());
        }

        using (var scope = site.Services.CreateScope())
        {
            var (open, url) = await scope.ServiceProvider.GetRequiredService<InstanceService>().SettingsAsync();
            Assert.False(open);
            Assert.Equal("https://todo.example.com", url);
        }

        // Only the keys that change; an empty address clears it.
        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { publicUrl = "" } })).StatusCode);
        using (var scope = site.Services.CreateScope())
        {
            var (open, url) = await scope.ServiceProvider.GetRequiredService<InstanceService>().SettingsAsync();
            Assert.False(open);
            Assert.Null(url);
        }

        Assert.Equal(HttpStatusCode.OK, (await api.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = true } })).StatusCode);
    }

    [DbFact]
    public async Task A_wrong_value_or_an_unknown_key_is_400_and_saves_nothing()
    {
        var api = site.Api();
        var wrong = await api.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = false, publicUrl = "ftp://files.example.com" } });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using (var doc = JsonDocument.Parse(await wrong.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("publicUrl", out _));
        }

        var unknown = await api.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = false, vapidPrivateKey = "x" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var typed = await api.PutAsJsonAsync("/api/configure", new { values = new { registrationOpen = "no" } });
        Assert.Equal(HttpStatusCode.BadRequest, typed.StatusCode);

        using var scope = site.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<InstanceService>().SettingsAsync()).RegistrationOpen);
    }

    [DbFact]
    public async Task The_clean_up_runs_and_an_unknown_or_link_action_is_404()
    {
        var api = site.Api();
        var response = await api.PostAsync("/api/actions/cleanUp", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("message").GetString()));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/api/actions/nothing", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsync("/api/actions/openApp", null)).StatusCode);
    }

    [DbFact]
    public async Task The_apis_of_the_app_stay_as_they_were()
    {
        // The webhook still answers its own way (an unknown token is 404), the app API still wants the cookie.
        var client = site.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/webhook/" + Guid.NewGuid(), new StringContent("Milch kaufen"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await site.Api().GetAsync("/api/app/parse?text=x")).StatusCode);
    }

    private async Task AddSampleDataAsync()
    {
        using var scope = site.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.SingleAsync(u => u.Email == MatDoFactory.AdminEmail);
        var (other, _) = await accounts.RegisterAsync("someone@example.com", "Someone", "Secret-Passw0rd-1");
        var (inactive, _) = await accounts.RegisterAsync("gone@example.com", "Gone", "Secret-Passw0rd-2");
        inactive!.IsActive = false;

        var inbox = await db.Projects.SingleAsync(p => p.OwnerId == admin.Id && p.IsInbox);
        var team = new Team { Name = "Team" };
        db.Teams.Add(team);
        db.Projects.Add(new Project { OwnerId = admin.Id, Name = "Garden" });
        db.Projects.Add(new Project { OwnerId = admin.Id, Name = "Old", IsArchived = true });

        var today = DateOnly.FromDateTime(DateTime.Now);
        db.Tasks.AddRange(
            new TaskItem { ProjectId = inbox.Id, Content = "Open" },
            new TaskItem { ProjectId = inbox.Id, Content = "Overdue", DueDate = today.AddDays(-2) },
            new TaskItem { ProjectId = inbox.Id, Content = "Later", DueDate = today.AddDays(3) },
            new TaskItem { ProjectId = inbox.Id, Content = "Done", IsCompleted = true, CompletedDate = DateTime.UtcNow.AddDays(-1) },
            new TaskItem { ProjectId = inbox.Id, Content = "Done long ago", IsCompleted = true, CompletedDate = DateTime.UtcNow.AddDays(-30) },
            new TaskItem { ProjectId = inbox.Id, Content = "Deleted", UpdateState = SoftEntity.Deleted });
        db.Webhooks.AddRange(
            new WebhookToken { UserId = admin.Id, Name = "Phone" },
            new WebhookToken { UserId = other!.Id, Name = "Off", IsActive = false });
        db.PushDevices.Add(new PushDevice { UserId = admin.Id, Endpoint = "https://fcm.googleapis.com/fcm/send/x", P256dh = "key", Auth = "device-secret", Name = "Test" });
        await db.SaveChangesAsync();
    }
}

/// <summary>Without <c>MAT_API_KEY</c> nobody gets in with a key, while <c>/api</c> and <c>/api/health</c> still answer.</summary>
public sealed class PlatformWithoutKeyTests(NoKeyFactory site) : IClassFixture<NoKeyFactory>
{
    [DbFact]
    public async Task Without_a_key_the_protected_endpoints_are_401_and_the_definition_says_so()
    {
        var client = site.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", MatDoFactory.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/configure")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api"));
        Assert.False(doc.RootElement.GetProperty("auth").GetProperty("apiKey").GetBoolean());
    }
}
