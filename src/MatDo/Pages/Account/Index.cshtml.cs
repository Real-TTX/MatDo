using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account;

/// <summary>The profile: name, time zone, what the quick add may create, the look. The theme button of the menu saves here too (handler Theme).</summary>
public sealed class IndexModel(AppDbContext db) : AppPageModel
{
    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string TimeZone { get; set; } = string.Empty;

    [BindProperty] public bool AutoCreateProjects { get; set; }

    [BindProperty] public bool AutoCreateLabels { get; set; }

    [BindProperty] public string Scheme { get; set; } = "standard";

    public string Email { get; private set; } = string.Empty;

    public List<Choice> Zones { get; } = Dates.ZoneIds.Select(z => new Choice(z, z.Replace('_', ' '))).ToList();

    public List<Choice> Schemes { get; } =
    [
        new("standard", "Standard"), new("paper", "Papier (warm)"), new("contrast", "Hoher Kontrast"), new("oled", "Schwarz (OLED, nur dunkel)"),
    ];

    public async Task OnGetAsync()
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == UserId);
        DisplayName = user.DisplayName;
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
            ModelState.AddModelError(nameof(DisplayName), "Bitte einen Namen eingeben.");
        }

        if (!Dates.IsZone(TimeZone))
        {
            ModelState.AddModelError(nameof(TimeZone), "Bitte eine Zeitzone aus der Liste wählen.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = DisplayName.Trim().Length > 120 ? DisplayName.Trim()[..120] : DisplayName.Trim();
        user.TimeZone = TimeZone;
        user.AutoCreateProjects = AutoCreateProjects;
        user.AutoCreateLabels = AutoCreateLabels;
        user.Scheme = Scheme is "paper" or "contrast" or "oled" ? Scheme : "standard";
        await db.SaveChangesAsync();
        SessionService.Invalidate();
        Flash("Gespeichert.");
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
