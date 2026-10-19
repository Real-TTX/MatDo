using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace MatDo.Pages.Account;

/// <summary>Registering. The first account of an installation is its administrator; later accounts only while the administrator keeps the registration open.</summary>
[EnableRateLimiting("signin")]
public sealed class RegisterModel(AccountService accounts, SessionService sessions) : PageModel
{
    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public string TimeZone { get; set; } = "Europe/Berlin";

    public bool First { get; private set; }

    public bool Open { get; private set; }

    private async Task LoadAsync()
    {
        First = !await accounts.AnyUserAsync();
        Open = First || (await accounts.SettingsAsync()).RegistrationOpen;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!Open)
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ModelState.AddModelError(nameof(DisplayName), Text.T("Account.Name.Required"));
            return Page();
        }

        var (user, error) = await accounts.RegisterAsync(Email, DisplayName, Password, TimeZone, language: Languages.Current);
        if (user is null)
        {
            ModelState.AddModelError(error!.Field, error.Message);
            return Page();
        }

        await sessions.SignInAsync(HttpContext, user, remember: true);
        TempData["Flash"] = Text.T("Account.Register.Welcome");
        return LocalRedirect("/Today");
    }
}
