using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using MatDo.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account;

/// <summary>The data of the person (GDPR): download everything as JSON (Art. 15/20), and delete the account for good (Art. 17) after the password.</summary>
public sealed class DataModel(AppDbContext db, AccountDataService data, PasswordService passwords, SessionService sessions) : AppPageModel
{
    [BindProperty] public string Password { get; set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public async Task OnGetAsync() => Email = await db.Users.Where(u => u.Id == UserId).Select(u => u.Email).FirstAsync();

    /// <summary>The export: a file to keep or to take to another app.</summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        var json = await data.ExportAsync(UserId, HttpContext.RequestAborted);
        return File(json, "application/json", $"matdo-{Dates.Iso(Today)}.json");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        Email = user.Email;
        if (!passwords.Verify(user.PasswordHash, Password))
        {
            ModelState.AddModelError(nameof(Password), Text.T("Data.Delete.WrongPassword"));
            return Page();
        }

        if (await data.IsLastAdminAsync(user.Id))
        {
            ModelState.AddModelError(string.Empty, Text.T("Data.Delete.LastAdmin"));
            return Page();
        }

        var id = user.Id;
        await sessions.SignOutAsync(HttpContext);
        await data.EraseAsync(id);
        TempData["Flash"] = Text.T("Data.Delete.Done");
        return LocalRedirect("/Account/Login");
    }
}
