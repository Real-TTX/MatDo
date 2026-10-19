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
            ModelState.AddModelError(nameof(DisplayName), "Bitte einen Namen eingeben.");
            return Page();
        }

        var (user, error) = await accounts.RegisterAsync(Email, DisplayName, Password, TimeZone);
        if (user is null)
        {
            ModelState.AddModelError(error!.Contains("Passwort", StringComparison.Ordinal) ? nameof(Password) : nameof(Email), error);
            return Page();
        }

        await sessions.SignInAsync(HttpContext, user, remember: true);
        TempData["Flash"] = "Willkommen bei MatDo! Probiere die Schnelleingabe: „Milch kaufen morgen 17 Uhr p1“.";
        return LocalRedirect("/Today");
    }
}
