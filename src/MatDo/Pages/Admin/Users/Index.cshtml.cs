using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Admin.Users;

public sealed record UserRow(long Id, string DisplayName, string Email, UserRole Role, bool IsActive, bool Locked, DateTime? LastLogin, DateTime Created, int Tasks);

/// <summary>The accounts of the installation. An administrator sees how many tasks a person has, never which.</summary>
public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    private const int PageSize = 25;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<UserRow> Items { get; private set; } = [];

    public string Zone => Me.TimeZone;

    public async Task OnGetAsync()
    {
        var search = ListQuery.Text(Request, "q");
        var role = ListQuery.Text(Request, "role");
        var state = ListQuery.Text(Request, "state");
        var sort = ListQuery.Text(Request, "sort") ?? "name";
        var desc = ListQuery.Descending(Request);
        var now = DateTime.UtcNow;

        var query = db.Users.AsNoTracking();
        if (search is not null)
        {
            var s = search.ToLower();
            query = query.Where(u => u.DisplayName.ToLower().Contains(s) || u.Email.Contains(s));
        }

        if (role == "admin")
        {
            query = query.Where(u => u.Role == UserRole.Admin);
        }
        else if (role == "user")
        {
            query = query.Where(u => u.Role == UserRole.User);
        }

        if (state == "active")
        {
            query = query.Where(u => u.IsActive);
        }
        else if (state == "disabled")
        {
            query = query.Where(u => !u.IsActive);
        }

        query = (sort, desc) switch
        {
            ("email", false) => query.OrderBy(u => u.Email),
            ("email", true) => query.OrderByDescending(u => u.Email),
            ("login", false) => query.OrderBy(u => u.LastLoginDate),
            ("login", true) => query.OrderByDescending(u => u.LastLoginDate),
            ("created", false) => query.OrderBy(u => u.CreateDate),
            ("created", true) => query.OrderByDescending(u => u.CreateDate),
            (_, true) => query.OrderByDescending(u => u.DisplayName),
            _ => query.OrderBy(u => u.DisplayName),
        };

        var total = await db.Users.CountAsync();
        var matching = await query.CountAsync();
        var page = ListQuery.Page(Request);
        Items = (await query.Skip((page - 1) * PageSize).Take(PageSize)
                .Select(u => new
                {
                    u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.LockoutUntil, u.LastLoginDate, u.CreateDate,
                    Tasks = db.Tasks.Count(t => t.Project!.OwnerId == u.Id),
                })
                .ToListAsync())
            .Select(u => new UserRow(u.Id, u.DisplayName, u.Email, u.Role, u.IsActive, u.LockoutUntil > now, u.LastLoginDate, u.CreateDate, u.Tasks)).ToList();

        Toolbar = new ToolbarModel
        {
            Id = "users",
            Action = "/Admin/Users",
            Label = Text.T("Users.Title"),
            Search = search,
            SearchField = true,
            SearchPlaceholder = Text.T("Users.Search"),
            Filters =
            [
                new FilterField("role", Text.T("Users.Col.Role"), [new("admin", Text.T("Common.Role.Admin")), new("user", Text.T("Common.Role.User"))], role),
                new FilterField("state", Text.T("Common.Status"), [new("active", Text.T("Common.Active")), new("disabled", Text.T("Users.Disabled"))], state),
            ],
            SortOptions = [new("name", Text.T("Common.Name")), new("email", Text.T("Account.Email")), new("login", Text.T("Users.Col.LastLogin")), new("created", Text.T("Users.Registered"))],
            Sort = sort,
            Descending = desc,
            DefaultSort = "name",
            Total = total,
            Matching = matching,
            Noun = Text.T("Users.Many"),
            NounOne = Text.T("Users.One"),
        };
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = matching, Url = Toolbar.UrlWithPage };
    }
}
