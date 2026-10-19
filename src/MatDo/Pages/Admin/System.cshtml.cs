using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Admin;

/// <summary>The settings of the installation (registration, public address) and what is in it.</summary>
public sealed class SystemModel(AppDbContext db, AccountService accounts) : AppPageModel
{
    [BindProperty] public bool RegistrationOpen { get; set; }

    [BindProperty] public string PublicUrl { get; set; } = string.Empty;

    public (int Users, int Teams, int Projects, int Open, int Done, int Webhooks) Counts { get; private set; }

    public string Database { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var s = await accounts.SettingsAsync();
        RegistrationOpen = s.RegistrationOpen;
        PublicUrl = s.PublicUrl ?? string.Empty;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var url = PublicUrl.Trim().TrimEnd('/');
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            ModelState.AddModelError(nameof(PublicUrl), "Bitte eine vollständige Adresse wie https://todo.example.com eingeben, oder leer lassen.");
            await LoadAsync();
            return Page();
        }

        var s = await accounts.SettingsAsync();
        s.RegistrationOpen = RegistrationOpen;
        s.PublicUrl = url.Length == 0 ? null : url;
        await db.SaveChangesAsync();
        Flash("Gespeichert.");
        return LocalRedirect("/Admin/System");
    }

    private async Task LoadAsync()
    {
        Counts = (await db.Users.CountAsync(), await db.Teams.CountAsync(), await db.Projects.CountAsync(),
            await db.Tasks.CountAsync(t => !t.IsCompleted), await db.Tasks.CountAsync(t => t.IsCompleted), await db.Webhooks.CountAsync());
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Database = $"PostgreSQL {cs.Host}:{cs.Port}/{cs.Database}";
    }
}
