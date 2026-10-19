using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Labels;

public sealed record LabelRow(long Id, string Name, string Color, bool IsFavorite, int Open);

public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    private const int PageSize = 50;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<LabelRow> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var search = ListQuery.Text(Request, "q");
        var state = ListQuery.Text(Request, "state");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var desc = ListQuery.Descending(Request);

        var all = (await db.Labels.Where(l => l.OwnerId == UserId)
                .Select(l => new { l.Id, l.Name, l.Color, l.IsFavorite, Open = db.TaskLabels.Count(x => x.LabelId == l.Id && !x.Task!.IsCompleted && x.Task.UpdateState != SoftEntity.Deleted) })
                .AsNoTracking().ToListAsync())
            .Select(l => new LabelRow(l.Id, l.Name, l.Color, l.IsFavorite, l.Open)).ToList();

        IEnumerable<LabelRow> list = all;
        if (search is not null)
        {
            list = list.Where(l => l.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        list = state switch
        {
            "favorite" => list.Where(l => l.IsFavorite),
            "used" => list.Where(l => l.Open > 0),
            "unused" => list.Where(l => l.Open == 0),
            _ => list,
        };
        list = sort == "open" ? list.OrderBy(l => l.Open).ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase) : list.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase);
        var filtered = list.ToList();
        if (desc)
        {
            filtered.Reverse();
        }

        Toolbar = new ToolbarModel
        {
            Id = "labels",
            Action = "/Labels",
            Label = Text.T("Labels.Title"),
            Search = search,
            SearchField = true,
            SearchPlaceholder = Text.T("Labels.Search"),
            Filters = [new FilterField("state", Text.T("Common.Status"), [new("favorite", Text.T("Menu.Favorites")), new("used", Text.T("Labels.Used")), new("unused", Text.T("Labels.Unused"))], state)],
            SortOptions = [new("name", Text.T("Common.Name")), new("open", Text.T("Labels.Col.Open"))],
            Sort = sort,
            Descending = desc,
            DefaultSort = "name",
            Total = all.Count,
            Matching = filtered.Count,
            Noun = Text.T("Labels.Many"),
            NounOne = Text.T("Labels.One"),
        };
        var page = ListQuery.Page(Request);
        Items = ListQuery.Slice(filtered, page, PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = filtered.Count, Url = Toolbar.UrlWithPage };
    }
}
