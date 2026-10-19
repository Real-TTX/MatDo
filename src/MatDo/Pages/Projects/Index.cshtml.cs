using System.Globalization;
using MatDo.Services;

namespace MatDo.Pages.Projects;

/// <summary>The list of projects: the toolbar (search, filter by team and state, sort), the table, the pager, the list actions.</summary>
public sealed class IndexModel(ProjectService projects, TeamService teams) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<ProjectInfo> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var all = (await projects.ListAsync(UserId, includeArchived: true)).Where(p => !p.IsInbox).ToList();
        var search = ListQuery.Text(Request, "q");
        var team = ListQuery.Text(Request, "team");
        var state = ListQuery.Text(Request, "state");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var desc = ListQuery.Descending(Request);

        IEnumerable<ProjectInfo> list = all;
        if (search is not null)
        {
            list = list.Where(p => p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        if (team == "personal")
        {
            list = list.Where(p => p.TeamId is null);
        }
        else if (long.TryParse(team, out var teamId))
        {
            list = list.Where(p => p.TeamId == teamId);
        }

        list = state switch
        {
            "archived" => list.Where(p => p.IsArchived),
            "favorite" => list.Where(p => p.IsFavorite && !p.IsArchived),
            "all" => list,
            _ => list.Where(p => !p.IsArchived),
        };

        list = sort switch
        {
            "open" => list.OrderBy(p => p.Open).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            "team" => list.OrderBy(p => p.TeamName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        var filtered = list.ToList();
        if (desc)
        {
            filtered.Reverse();
        }

        var myTeams = await teams.ListAsync(UserId);
        Toolbar = new ToolbarModel
        {
            Id = "projects",
            Action = "/Projects",
            Label = "Projekte",
            Search = search,
            SearchField = true,
            SearchPlaceholder = "Projekte durchsuchen",
            Filters =
            [
                new FilterField("team", "Team", new[] { new FilterOption("personal", "Persönlich") }.Concat(myTeams.Select(t => new FilterOption(t.Id.ToString(CultureInfo.InvariantCulture), t.Name))).ToList(), team),
                new FilterField("state", "Status", [new("favorite", "Favoriten"), new("archived", "Archiviert"), new("all", "Alle inkl. archivierte")], state),
            ],
            SortOptions = [new("name", "Name"), new("team", "Team"), new("open", "Offene Aufgaben")],
            Sort = sort,
            Descending = desc,
            DefaultSort = "name",
            Total = all.Count(p => !p.IsArchived),
            Matching = filtered.Count,
            Noun = "Projekte",
            NounOne = "Projekt",
        };

        var page = ListQuery.Page(Request);
        Items = ListQuery.Slice(filtered, page, PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = filtered.Count, Url = Toolbar.UrlWithPage };
    }
}
