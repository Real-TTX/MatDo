using MatDo.Localization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account.Keywords;

public sealed class EditModel(AppDbContext db) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public KeywordKind Kind { get; set; } = KeywordKind.Project;

    [BindProperty] public string Word { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public List<Choice> Kinds { get; } = KeywordKinds.All.Select(k => new Choice(k.ToString(), KeywordKinds.Name(k), KeywordKinds.Example(k))).ToList();

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var k = await db.Keywords.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
            if (k is null)
            {
                return NotFound();
            }

            Kind = k.Kind;
            Word = k.Word;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        UserKeyword? keyword = null;
        if (Id is { } id)
        {
            keyword = await db.Keywords.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
            if (keyword is null)
            {
                return NotFound();
            }
        }

        var word = string.Join(' ', Word.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (word.Length == 0)
        {
            ModelState.AddModelError(nameof(Word), Text.T("Keywords.Word.Required"));
        }
        else if (word.Length > 40 || word.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '-'))
        {
            ModelState.AddModelError(nameof(Word), Text.T("Keywords.Word.Invalid"));
        }
        else if (!Enum.IsDefined(Kind))
        {
            ModelState.AddModelError(nameof(Kind), Text.T("Keywords.Kind.Required"));
        }
        else
        {
            var lower = word.ToLower();
            if (await db.Keywords.AnyAsync(x => x.UserId == UserId && x.Word.ToLower() == lower && x.Id != (Id ?? 0)))
            {
                ModelState.AddModelError(nameof(Word), Text.T("Keywords.Word.Taken"));
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (keyword is null)
        {
            keyword = new UserKeyword { UserId = UserId };
            db.Keywords.Add(keyword);
        }

        keyword.Kind = Kind;
        keyword.Word = word;
        await db.SaveChangesAsync();
        Flash(Text.T("Common.SavedNamed", word));
        return LocalRedirect("/Account/Keywords");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var k = await db.Keywords.FirstOrDefaultAsync(x => x.Id == Id && x.UserId == UserId);
        if (k is null)
        {
            return NotFound();
        }

        db.Keywords.Remove(k);
        await db.SaveChangesAsync();
        Flash(Text.T("Common.DeletedNamed", k.Word));
        return LocalRedirect("/Account/Keywords");
    }
}
