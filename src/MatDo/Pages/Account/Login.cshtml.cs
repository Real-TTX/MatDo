using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace MatDo.Pages.Account;

[EnableRateLimiting("signin")]
public sealed class LoginModel(AccountService accounts, SessionService sessions) : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public bool Remember { get; set; } = true;

    [BindProperty(SupportsGet = true)] public string ReturnUrl { get; set; } = string.Empty;

    public bool CanRegister { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await accounts.AnyUserAsync())
        {
            return LocalRedirect("/Account/Register");
        }

        CanRegister = (await accounts.SettingsAsync()).RegistrationOpen;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        CanRegister = (await accounts.SettingsAsync()).RegistrationOpen;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ModelState.AddModelError(nameof(Email), Text.T("Account.Login.BothRequired"));
            return Page();
        }

        var result = await accounts.SignInAsync(Email, Password);
        if (result.User is null)
        {
            ModelState.AddModelError(nameof(Email), result.Error!);
            return Page();
        }

        await sessions.SignInAsync(HttpContext, result.User, Remember);
        return !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : LocalRedirect("/Today");
    }
}
