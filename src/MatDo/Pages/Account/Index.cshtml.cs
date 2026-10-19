using MatDo.Controls;
using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account;

/// <summary>The profile: name, language, time zone, the look, what the quick add may create. The theme button of the menu saves here too (handler Theme).</summary>
public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Language { get; set; } = string.Empty;

    [BindProperty] public string TimeZone { get; set; } = string.Empty;

    [BindProperty] public bool AutoCreateProjects { get; set; }

    [BindProperty] public bool AutoCreateLabels { get; set; }

    [BindProperty] public string Scheme { get; set; } = "standard";

    public string Email { get; private set; } = string.Empty;

    public List<Choice> Zones { get; } = Dates.ZoneIds.Select(z => new Choice(z, z.Replace('_', ' '))).ToList();

    public List<Choice> LanguageChoices { get; } = [new(string.Empty, Text.T("Common.Language.Auto")), .. Languages.Supported.Select(l => new Choice(l.Code, l.Name))];

    public List<Choice> Schemes { get; } =
    [
        new("standard", Text.T("Settings.Scheme.Standard")), new("paper", Text.T("Settings.Scheme.Paper")),
        new("contrast", Text.T("Settings.Scheme.Contrast")), new("oled", Text.T("Settings.Scheme.Oled")),
    ];

    public async Task OnGetAsync()
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
        DisplayName = user.DisplayName;
        Language = user.Language;
        TimeZone = user.TimeZone;
        AutoCreateProjects = user.AutoCreateProjects;
        AutoCreateLabels = user.AutoCreateLabels;
        Scheme = user.Scheme;
        Email = user.Email;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await db.Users.FirstAsync(u => u.Id == UserId);
        Email = user.Email;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ModelState.AddModelError(nameof(DisplayName), Text.T("Account.Name.Required"));
        }

        if (!Dates.IsZone(TimeZone))
        {
            ModelState.AddModelError(nameof(TimeZone), Text.T("Settings.TimeZone.Invalid"));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = DisplayName.Trim().Length > 120 ? DisplayName.Trim()[..120] : DisplayName.Trim();
        user.Language = Languages.IsSupported(Language) ? Languages.Normalize(Language) : string.Empty;
        user.TimeZone = TimeZone;
        user.AutoCreateProjects = AutoCreateProjects;
        user.AutoCreateLabels = AutoCreateLabels;
        user.Scheme = Scheme is "paper" or "contrast" or "oled" ? Scheme : "standard";
        await db.SaveChangesAsync();
        if (user.Language.Length > 0)
        {
            Languages.SetCookie(Response, user.Language, Request.IsHttps);
        }
        else
        {
            Response.Cookies.Delete(Languages.CookieName);
        }

        SessionService.Invalidate();

        // the message is written in the language just chosen
        Flash(user.Language.Length > 0 ? Languages.In(user.Language, () => Text.T("Common.Saved")) : Text.T("Common.Saved"));
        return LocalRedirect("/Account");
    }

    /// <summary>The theme button of the menu (system, light, dark), saved per person.</summary>
    public async Task<IActionResult> OnPostThemeAsync(string theme)
    {
        if (theme is "system" or "light" or "dark")
        {
            await db.Users.Where(u => u.Id == UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Theme, theme));
            SessionService.Invalidate();
        }

        return new NoContentResult();
    }
}
