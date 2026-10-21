using System.Text.Json;
using MatDo.Services;
using MatPlatform;

namespace MatDo.Platform;

/// <summary>
/// MatDo as an app of the platform contract (MatDesign docs/PLATFORM.md) on the shared package MatPlatform (the folder <c>MatPlatform/</c>, a copy):
/// how a consumer such as Mattiverse shows MatDo (<see cref="Describe"/>: tiles, the few settings, buttons), its values and what its buttons do.
/// The settings are the two of the admin page System (registration, public address), kept in the same place (<see cref="InstanceService"/>).
/// The values come from the services (no DbContext here: this is the web layer).
/// </summary>
public sealed class MatDoPlatform(InstanceService instance, ILogger<MatDoPlatform> log)
    : IMatPlatformState, IMatPlatformConfiguration, IMatPlatformActions, IMatPlatformHealth
{
    /// <summary>The id of MatDo in the platform (and the default client id at MatIdentity), as in MatDesign <c>AppRegistry.cs</c>.</summary>
    public const string AppId = "matdo";

    /// <summary>The accent of MatDo in MatDesign <c>AppRegistry.cs</c>.</summary>
    public const string Accent = "#8300bc";

    /// <summary>The MatApiDefinition of MatDo (<c>GET /api</c>); <c>auth</c> and <c>endpoints</c> are set by MatPlatform.</summary>
    public static void Describe(MatPlatformBuilder o)
    {
        var T = o.T;

        o.App(AppId, "MatDo", T("Aufgaben, Projekte und Teams", "Tasks, projects and teams"), AppInfo.Version, "/img/logo/matdo-mark.svg", Accent, "/Today");

        o.Page("overview", T("Übersicht", "Overview"), "dashboard",
            MatSection.Tiles("users", "teams", "projects", "openTasks", "overdueTasks", "doneLast7Days", "webhooks", "pushDevices", "registration", "lastActivity"),
            MatSection.Actions("openApp", "openUsers", "openSystem", "cleanUp"));
        o.Page("settings", T("Einstellungen", "Settings"), "settings",
            MatSection.Form(T("Allgemein", "General"), "registrationOpen", "publicUrl"));

        o.Tile("users", T("Aktive Benutzer", "Active users"), icon: "users");
        o.Tile("teams", T("Teams", "Teams"), icon: "layers");
        o.Tile("projects", T("Projekte", "Projects"), icon: "folder");
        o.Tile("openTasks", T("Offene Aufgaben", "Open tasks"), icon: "check-square");
        o.Tile(new MatStateItem("overdueTasks", T("Überfällige Aufgaben", "Overdue tasks")) { Icon = "alert" });
        o.Tile("doneLast7Days", T("Erledigt in 7 Tagen", "Done in 7 days"), icon: "check-circle");
        o.Tile("webhooks", T("Aktive Webhooks", "Active webhooks"), icon: "plug");
        o.Tile("pushDevices", T("Geräte mit Benachrichtigungen", "Devices with notifications"), icon: "smartphone");
        o.Tile("registration", T("Registrierung", "Sign-up"), "status", icon: "user");
        o.Tile("lastActivity", T("Letzte Änderung an einer Aufgabe", "Last change to a task"), "time", icon: "activity");

        o.Field(new MatField("registrationOpen", T("Registrierung offen", "Open sign-up"), "bool")
        {
            Hint = T("Aus: nur Administratoren legen Konten an.", "Off: only administrators create accounts."),
        });
        o.Field(new MatField("publicUrl", T("Öffentliche Adresse", "Public address"), "url")
        {
            Max = 300,
            Placeholder = "https://todo.example.com",
            Hint = T("Für die Adressen der Webhooks; leer: die Adresse des Aufrufs.", "For the addresses of the webhooks; empty: the address of the request."),
        });

        o.LinkAction("openApp", T("Öffnen", "Open"), "external", "/Today");
        o.LinkAction("openUsers", T("Benutzer", "Users"), "users", "/Admin/Users");
        o.LinkAction("openSystem", T("System", "System"), "settings", "/Admin/System");
        o.PostAction(
            "cleanUp",
            T("Aufräumen", "Clean up"),
            "broom",
            T("Beendete Sitzungen und vor mehr als 30 Tagen gelöschte Aufgaben und Kommentare jetzt endgültig entfernen?",
              "Remove ended sessions and the tasks and comments deleted more than 30 days ago for good now?"));

        o.Role("admin", T("Administrator", "Administrator"));
        o.Role("user", T("Benutzer", "User"));
    }

    public async Task<IReadOnlyList<MatHealthCheck>> CheckHealthAsync(MatPlatformCall call, CancellationToken ct)
    {
        if (!await instance.CanConnectAsync(ct))
        {
            return [new MatHealthCheck("database", "down", call.T("Die Datenbank ist nicht erreichbar.", "The database cannot be reached."))];
        }

        var checks = new List<MatHealthCheck> { new("database", "ok") };
        if (await instance.AnyUserAsync() && (await instance.SnapshotAsync(ct)).Admins == 0)
        {
            checks.Add(new MatHealthCheck("administrators", "degraded", call.T("Kein aktiver Administrator.", "No active administrator.")));
        }

        return checks;
    }

    public async Task<IReadOnlyDictionary<string, MatStateValue>> GetStateAsync(MatPlatformCall call, CancellationToken ct)
    {
        var s = await instance.SnapshotAsync(ct);
        var (open, _) = await instance.SettingsAsync();
        return new Dictionary<string, MatStateValue>
        {
            ["users"] = new(s.Users),
            ["teams"] = new(s.Teams),
            ["projects"] = new(s.Projects),
            ["openTasks"] = new(s.OpenTasks),
            ["overdueTasks"] = new(s.OverdueTasks, s.OverdueTasks > 0 ? "warn" : "ok"),
            ["doneLast7Days"] = new(s.DoneLast7Days),
            ["webhooks"] = new(s.ActiveWebhooks),
            ["pushDevices"] = new(s.PushDevices),
            ["registration"] = open
                ? new MatStateValue("info", Text: call.T("Offen", "Open"))
                : new MatStateValue("ok", Text: call.T("Geschlossen", "Closed")),
            ["lastActivity"] = new(s.LastActivityUtc is { } last ? new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc)) : null),
        };
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetConfigurationAsync(MatPlatformCall call, CancellationToken ct)
    {
        var (open, url) = await instance.SettingsAsync();
        return new Dictionary<string, object?>
        {
            ["registrationOpen"] = open,
            ["publicUrl"] = url ?? string.Empty,
        };
    }

    /// <summary>The values are checked against the definition already (MatPlatform, <c>CheckValues</c>); the address is checked once more like on the admin page.</summary>
    public async Task<IReadOnlyDictionary<string, string>> ConfigureAsync(IReadOnlyDictionary<string, JsonElement> values, MatPlatformCall call, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        bool? open = null;
        string? url = null;
        foreach (var (key, value) in values)
        {
            switch (key)
            {
                case "registrationOpen" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    open = value.GetBoolean();
                    break;
                case "publicUrl" when value.ValueKind is JsonValueKind.String:
                    url = value.GetString()!;
                    break;
                case "publicUrl" when value.ValueKind is JsonValueKind.Null:
                    url = string.Empty;
                    break;
                default:
                    errors[key] = call.T("Dieser Wert passt nicht.", "This value does not fit.");
                    break;
            }
        }

        if (url is not null && InstanceService.NormalizePublicUrl(url) is { } normalized && !InstanceService.IsPublicUrl(normalized))
        {
            errors["publicUrl"] = call.T("Eine vollständige Adresse mit http:// oder https://.", "A complete address with http:// or https://.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        await instance.SaveSettingsAsync(open, url);

        // MatDo has no activity log of its own: the change (the keys, no values) goes to the log of the app.
        log.LogInformation("Settings changed through the platform API by {Caller}: {Keys}", call.Caller, string.Join(", ", values.Keys));
        return errors;
    }

    public async Task<MatActionResult?> RunActionAsync(string id, MatPlatformCall call, CancellationToken ct)
    {
        if (id != "cleanUp")
        {
            return null;
        }

        var result = await instance.CleanUpAsync(ct);
        log.LogInformation("Clean-up through the platform API by {Caller}: {Sessions} sessions, {Tasks} tasks, {Comments} comments", call.Caller, result.Sessions, result.Tasks, result.Comments);
        return new MatActionResult(true, call.T(
            $"Aufgeräumt: {result.Sessions} Sitzungen, {result.Tasks} Aufgaben, {result.Comments} Kommentare.",
            $"Cleaned up: {result.Sessions} sessions, {result.Tasks} tasks, {result.Comments} comments."));
    }
}
