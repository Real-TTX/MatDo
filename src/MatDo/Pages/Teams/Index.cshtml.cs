using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;

namespace MatDo.Pages.Teams;

public sealed class IndexModel(TeamService teams) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<TeamInfo> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var all = await teams.ListAsync(UserId);
        var search = ListQuery.Text(Request, "q");
        var role = ListQuery.Text(Request, "role");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var desc = ListQuery.Descending(Request);

        IEnumerable<TeamInfo> list = all;
        if (search is not null)
        {
            list = list.Where(t => t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || (t.Description ?? string.Empty).Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        list = role switch
        {
            "owner" => list.Where(t => t.Role == TeamRole.Owner),
            "member" => list.Where(t => t.Role == TeamRole.Member),
            _ => list,
        };
        list = sort switch
        {
            "members" => list.OrderBy(t => t.Members),
            "projects" => list.OrderBy(t => t.Projects),
            _ => list.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var filtered = list.ToList();
        if (desc)
        {
            filtered.Reverse();
        }

        Toolbar = new ToolbarModel
        {
            Id = "teams",
            Action = "/Teams",
            Label = Text.T("Teams.Title"),
            Search = search,
            SearchField = true,
            SearchPlaceholder = Text.T("Teams.Search"),
            Filters = [new FilterField("role", Text.T("Teams.Col.MyRole"), [new("owner", Text.T("Teams.Role.Owner")), new("member", Text.T("Teams.Role.Member"))], role)],
            SortOptions = [new("name", Text.T("Common.Name")), new("members", Text.T("Teams.Col.Members")), new("projects", Text.T("Projects.Title"))],
            Sort = sort,
            Descending = desc,
            DefaultSort = "name",
            Total = all.Count,
            Matching = filtered.Count,
            Noun = Text.T("Teams.Many"),
            NounOne = Text.T("Teams.Col.Team"),
        };
        var page = ListQuery.Page(Request);
        Items = ListQuery.Slice(filtered, page, PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = filtered.Count, Url = Toolbar.UrlWithPage };
    }
}
