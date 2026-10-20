using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

/// <summary>What an open window is told: the tasks of a project changed ("tasks"); task, title and source (webhook) for the note it shows.</summary>
public sealed record LiveEvent(string Kind, long? Project = null, long? Task = null, string? Title = null, string? Source = null);

/// <summary>
/// The open windows of each person (live.js, GET /api/app/events, Server-Sent Events) and the news for them: a task that came in by the webhook, or a change made on
/// another device, makes every window of the people who see the project reload its list at once. The window that made the change (its tab id, header
/// <c>X-MatDo-Tab</c>) is not told. One instance of the app: the windows are kept in memory.
/// </summary>
public sealed class LiveService(IServiceScopeFactory scopes, ILogger<LiveService> logger)
{
    public const string TabHeader = "X-MatDo-Tab";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, (string Tab, Channel<string> Channel)>> windows = new();

    /// <summary>A window opens its stream; it reads what is written to the channel until it goes.</summary>
    public (Guid Id, ChannelReader<string> Reader) Open(long userId, string tab)
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var id = Guid.NewGuid();
        windows.GetOrAdd(userId, _ => new()).TryAdd(id, (tab, channel));
        return (id, channel.Reader);
    }

    public void Close(long userId, Guid id)
    {
        if (windows.TryGetValue(userId, out var mine) && mine.TryRemove(id, out var w))
        {
            w.Channel.Writer.TryComplete();
        }
    }

    public int Count => windows.Values.Sum(w => w.Count);

    /// <summary>Tells every window of the people who see the project (the owner, or the members of its team), except the window that made the change.</summary>
    public async Task TasksChangedAsync(long projectId, LiveEvent e, string? exceptTab = null)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new { p.OwnerId, p.TeamId }).FirstOrDefaultAsync();
            if (project is null)
            {
                return;
            }

            var people = project.TeamId is { } team
                ? await db.TeamMembers.Where(m => m.TeamId == team).Select(m => m.UserId).ToListAsync()
                : [project.OwnerId];
            Send(people, e with { Project = projectId }, exceptTab);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The open windows could not be told about a change.");
        }
    }

    private void Send(IEnumerable<long> people, LiveEvent e, string? exceptTab)
    {
        var data = $"event: {e.Kind}\ndata: {JsonSerializer.Serialize(e, Json)}\n\n";
        foreach (var userId in people)
        {
            if (!windows.TryGetValue(userId, out var mine))
            {
                continue;
            }

            foreach (var (_, w) in mine)
            {
                if (string.IsNullOrEmpty(exceptTab) || w.Tab != exceptTab)
                {
                    w.Channel.Writer.TryWrite(data);
                }
            }
        }
    }
}
