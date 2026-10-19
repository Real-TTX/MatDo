using MatDo.Localization;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account;

/// <summary>A new password: the current one first. Every other session of the person ends.</summary>
public sealed class PasswordModel(AppDbContext db, PasswordService passwords, SessionService sessions) : AppPageModel
{
    [BindProperty] public string Current { get; set; } = string.Empty;

    [BindProperty] public string New { get; set; } = string.Empty;

    [BindProperty] public string Repeat { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        if (!passwords.Verify(user.PasswordHash, Current))
        {
            ModelState.AddModelError(nameof(Current), Text.T("Password.Current.Wrong"));
        }
        else if (PasswordService.Check(New, user.Email) is { } problem)
        {
            ModelState.AddModelError(nameof(New), problem);
        }
        else if (New != Repeat)
        {
            ModelState.AddModelError(nameof(Repeat), Text.T("Password.Mismatch"));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.PasswordHash = passwords.Hash(New);
        await db.SaveChangesAsync();
        await sessions.RevokeAllAsync(user.Id, SessionService.CurrentSessionId(HttpContext));
        Flash(Text.T("Password.Changed"));
        return LocalRedirect("/Account/Password");
    }
}
