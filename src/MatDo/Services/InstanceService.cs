using MatDo.Data;
using MatDo.Localization;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>The numbers of the installation (the platform tiles, the admin page System).</summary>
public sealed record InstanceSnapshot(
    int Users,
    int Admins,
    int Teams,
    int Projects,
    int OpenTasks,
    int OverdueTasks,
    int DoneTasks,
    int DoneLast7Days,
    int Webhooks,
    int ActiveWebhooks,
    int PushDevices,
    DateTime? LastActivityUtc);

/// <summary>What the housekeeping removed.</summary>
public sealed record CleanUpResult(int Sessions, int Tasks, int Comments);

/// <summary>
/// The installation as a whole: its numbers, the few settings of the system (registration, public address), the state of the database and the housekeeping.
/// The admin page System, the platform contract (<c>Platform/MatDoPlatform</c>) and the background housekeeping go through it, so none of them needs the database itself.
/// </summary>
public sealed class InstanceService(AppDbContext db, AccountService accounts)
{
    /// <summary>Can the database be reached (the health check)?</summary>
    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The numbers of the installation. "Overdue" counts the open tasks due before today in the zone of the server.</summary>
    public async Task<InstanceSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var lastTask = await db.Tasks.MaxAsync(t => (DateTime?)t.UpdateDate, ct);
        return new InstanceSnapshot(
            Users: await db.Users.CountAsync(u => u.IsActive, ct),
            Admins: await db.Users.CountAsync(u => u.IsActive && u.Role == UserRole.Admin, ct),
            Teams: await db.Teams.CountAsync(ct),
            Projects: await db.Projects.CountAsync(p => !p.IsArchived, ct),
            OpenTasks: await db.Tasks.CountAsync(t => !t.IsCompleted, ct),
            OverdueTasks: await db.Tasks.CountAsync(t => !t.IsCompleted && t.DueDate != null && t.DueDate < today, ct),
            DoneTasks: await db.Tasks.CountAsync(t => t.IsCompleted, ct),
            DoneLast7Days: await db.Tasks.CountAsync(t => t.IsCompleted && t.CompletedDate >= weekAgo, ct),
            Webhooks: await db.Webhooks.CountAsync(ct),
            ActiveWebhooks: await db.Webhooks.CountAsync(w => w.IsActive, ct),
            PushDevices: await db.PushDevices.CountAsync(ct),
            LastActivityUtc: lastTask);
    }

    /// <summary>Has anybody registered yet?</summary>
    public Task<bool> AnyUserAsync() => accounts.AnyUserAsync();

    /// <summary>Where the data is: <c>PostgreSQL host:port/database</c> (for the admin page; never in the platform API).</summary>
    public string DatabaseName()
    {
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        return $"PostgreSQL {cs.Host}:{cs.Port}/{cs.Database}";
    }

    /// <summary>The settings of the system.</summary>
    public async Task<(bool RegistrationOpen, string? PublicUrl)> SettingsAsync()
    {
        var s = await accounts.SettingsAsync();
        return (s.RegistrationOpen, s.PublicUrl);
    }

    /// <summary>
    /// Saves the settings of the system; a <c>null</c> leaves a value as it is, an empty address clears it. Answers the message for a public address that is not
    /// an absolute http(s) address (nothing is saved then), else <c>null</c>.
    /// </summary>
    public async Task<string?> SaveSettingsAsync(bool? registrationOpen, string? publicUrl)
    {
        string? url = null;
        if (publicUrl is not null)
        {
            url = NormalizePublicUrl(publicUrl);
            if (url is not null && !IsPublicUrl(url))
            {
                return Text.T("System.PublicUrl.Invalid");
            }
        }

        var s = await accounts.SettingsAsync();
        if (registrationOpen is { } open)
        {
            s.RegistrationOpen = open;
        }

        if (publicUrl is not null)
        {
            s.PublicUrl = url;
        }

        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>The address without spaces and the last slash; <c>null</c> when empty.</summary>
    public static string? NormalizePublicUrl(string? url)
    {
        var trimmed = (url ?? string.Empty).Trim().TrimEnd('/');
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static bool IsPublicUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>The housekeeping: ended and old sessions go, and what was deleted more than 30 days ago is removed for good.</summary>
    public async Task<CleanUpResult> CleanUpAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var limit = now.AddDays(-30);
        var sessions = await db.Sessions.Where(s => s.IsRevoked || s.ExpiresDate < now.AddDays(-1)).ExecuteDeleteAsync(ct);
        var comments = await db.Comments.IgnoreQueryFilters().Where(c => c.UpdateState == SoftEntity.Deleted && c.UpdateDate < limit).ExecuteDeleteAsync(ct);
        var tasks = await db.Tasks.IgnoreQueryFilters().Where(t => t.UpdateState == SoftEntity.Deleted && t.UpdateDate < limit).ExecuteDeleteAsync(ct);
        return new CleanUpResult(sessions, tasks, comments);
    }
}
