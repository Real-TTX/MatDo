using MatDo.Localization;
using System.Globalization;
using MatDo.Data;
using MatDo.Services;
using MatDo.Services.Parsing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account.Keywords;

/// <summary>
/// The keywords of the quick add and the webhook: a word that introduces a value ("Projekt EIMER"). The list with its toolbar, a box to try a sentence, and the
/// defaults to restore.
/// </summary>
public sealed class IndexModel(AppDbContext db, QuickAddService quick) : AppPageModel
{
    private const int PageSize = 50;

    public ToolbarModel Toolbar { get; private set; } = null!;

    public PagerModel Pager { get; private set; } = null!;

    public List<UserKeyword> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "try")] public string Try { get; set; } = string.Empty;

    public QuickAddPreview? Preview { get; private set; }

    public async Task OnGetAsync()
    {
        var all = await db.Keywords.Where(k => k.UserId == UserId).AsNoTracking().ToListAsync();
        var search = ListQuery.Text(Request, "q");
        var kind = ListQuery.Text(Request, "kind");
        var sort = ListQuery.Text(Request, "sort") ?? "kind";
        var desc = ListQuery.Descending(Request);

        IEnumerable<UserKeyword> list = all;
        if (search is not null)
        {
            list = list.Where(k => k.Word.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        if (Enum.TryParse<KeywordKind>(kind, out var k) && Enum.IsDefined(k))
        {
            list = list.Where(x => x.Kind == k);
        }

        list = sort == "word"
            ? list.OrderBy(x => x.Word, StringComparer.CurrentCultureIgnoreCase)
            : list.OrderBy(x => x.Kind).ThenBy(x => x.Word, StringComparer.CurrentCultureIgnoreCase);
        var filtered = list.ToList();
        if (desc)
        {
            filtered.Reverse();
        }

        Toolbar = new ToolbarModel
        {
            Id = "keywords",
            Action = "/Account/Keywords",
            Label = Text.T("Keywords.Title"),
            Search = search,
            SearchField = true,
            SearchPlaceholder = Text.T("Keywords.Search"),
            Filters = [new FilterField("kind", Text.T("Keywords.Col.Kind"), KeywordKinds.All.Select(x => new FilterOption(x.ToString(), KeywordKinds.Name(x))).ToList(), kind)],
            SortOptions = [new("kind", Text.T("Keywords.Col.Kind")), new("word", Text.T("Keywords.Col.Word"))],
            Sort = sort,
            Descending = desc,
            DefaultSort = "kind",
            Total = all.Count,
            Matching = filtered.Count,
            Noun = Text.T("Keywords.Many"),
            NounOne = Text.T("Keywords.One"),
        };
        var page = ListQuery.Page(Request);
        Items = ListQuery.Slice(filtered, page, PageSize).ToList();
        Pager = new PagerModel { Page = page, PageSize = PageSize, Total = filtered.Count, Url = Toolbar.UrlWithPage };

        if (!string.IsNullOrWhiteSpace(Try))
        {
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
            Preview = await quick.PreviewAsync(user, Try);
        }
    }

    /// <summary>Adds the default keywords that are missing (nothing of the person's own is removed).</summary>
    public async Task<IActionResult> OnPostDefaultsAsync()
    {
        var mine = await db.Keywords.Where(k => k.UserId == UserId).ToListAsync();
        var added = 0;
        foreach (var (kind, word) in AccountService.DefaultKeywords)
        {
            if (!mine.Any(k => k.Kind == kind && string.Equals(k.Word, word, StringComparison.OrdinalIgnoreCase)))
            {
                db.Keywords.Add(new UserKeyword { UserId = UserId, Kind = kind, Word = word });
                added++;
            }
        }

        await db.SaveChangesAsync();
        Flash(added == 0 ? Text.T("Keywords.Defaults.None") : Text.T("Keywords.Defaults.Added", added));
        return LocalRedirect("/Account/Keywords");
    }

    public string Format(DateOnly? d, TimeOnly? t) => d is null ? "–" : Dates.Long(d.Value, d.Value.Year != Today.Year) + (t is null ? string.Empty : " " + Dates.Time(t.Value));

    public static string Num(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "–";
}
