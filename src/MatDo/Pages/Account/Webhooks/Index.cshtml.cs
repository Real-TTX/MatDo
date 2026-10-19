using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account.Webhooks;

public sealed record WebhookRow(long Id, string Name, string Target, bool IsActive, DateTime? LastUsed, int Uses);

public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<WebhookRow> Items { get; private set; } = [];

    public string Zone => Me.TimeZone;

    public async Task OnGetAsync()
    {
        var all = (await db.Webhooks.Where(w => w.UserId == UserId)
                .Select(w => new { w.Id, w.Name, w.IsActive, w.LastUsedDate, w.UseCount, Project = db.Projects.Where(p => p.Id == w.ProjectId).Select(p => p.IsInbox ? "Eingang" : p.Name).FirstOrDefault() })
                .AsNoTracking().ToListAsync())
            .Select(w => new WebhookRow(w.Id, w.Name, w.Project ?? "Eingang", w.IsActive, w.LastUsedDate, w.UseCount)).ToList();
        var search = ListQuery.Text(Request, "q");
        var state = ListQuery.Text(Request, "state");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var desc = ListQuery.Descending(Request);

        IEnumerable<WebhookRow> list = all;
        if (search is not null)
        {
            list = list.Where(w => w.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        list = state switch
        {
            "on" => list.Where(w => w.IsActive),
            "off" => list.Where(w => !w.IsActive),
            _ => list,
        };
        list = sort switch
        {
            "used" => list.OrderBy(w => w.LastUsed ?? DateTime.MinValue),
            "uses" => list.OrderBy(w => w.Uses),
            _ => list.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var filtered = list.ToList();
        if (desc)
        {
            filtered.Reverse();
        }

        Toolbar = new ToolbarModel
        {
            Id = "webhooks",
            Action = "/Account/Webhooks",
            Label = "Webhooks",
            Search = search,
            SearchField = true,
            SearchPlaceholder = "Webhooks durchsuchen",
            Filters = [new FilterField("state", "Status", [new("on", "Aktiv"), new("off", "Abgeschaltet")], state)],
            SortOptions = [new("name", "Name"), new("used", "Zuletzt benutzt"), new("uses", "Aufrufe")],
            Sort = sort,
            Descending = desc,
            DefaultSort = "name",
            Total = all.Count,
            Matching = filtered.Count,
            Noun = "Webhooks",
            NounOne = "Webhook",
        };
        var page = ListQuery.Page(Request);
        Items = ListQuery.Slice(filtered, page, PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = filtered.Count, Url = Toolbar.UrlWithPage };
    }
}
