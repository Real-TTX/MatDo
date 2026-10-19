using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatDo.Pages.Account;

public sealed class LogoutModel(SessionService sessions) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/Today");

    public async Task<IActionResult> OnPostAsync()
    {
        await sessions.SignOutAsync(HttpContext);
        return LocalRedirect("/Account/Login");
    }
}
