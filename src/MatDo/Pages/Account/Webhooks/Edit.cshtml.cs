using System.Globalization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Account.Webhooks;

/// <summary>A webhook: its name, where its tasks go when the text names no project, on or off; its address with the instructions; a new address; delete.</summary>
public sealed class EditModel(AppDbContext db, ProjectService projects, AccountService accounts) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string Name { get; set; } = string.Empty;

    [BindProperty] public string ProjectId { get; set; } = string.Empty;

    [BindProperty] public bool IsActive { get; set; } = true;

    public WebhookToken? Hook { get; private set; }

    public string HookUrl { get; private set; } = string.Empty;

    public string BaseUrl { get; private set; } = string.Empty;

    public List<Choice> Projects { get; private set; } = [];

    public bool IsNew => Id is null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            Hook = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && w.UserId == UserId);
            if (Hook is null)
            {
                return NotFound();
            }

            Name = Hook.Name;
            ProjectId = Hook.ProjectId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            IsActive = Hook.IsActive;
        }
        else
        {
            Name = "iPhone Kurzbefehl";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        WebhookToken? hook = null;
        if (Id is { } id)
        {
            hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.UserId == UserId);
            if (hook is null)
            {
                return NotFound();
            }
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            ModelState.AddModelError(nameof(Name), "Bitte einen Namen eingeben.");
        }

        long? project = null;
        if (long.TryParse(ProjectId, out var p))
        {
            var found = await projects.FindAsync(UserId, p);
            if (found is null)
            {
                ModelState.AddModelError(nameof(ProjectId), "Dieses Projekt gibt es nicht.");
            }
            else if (!found.IsInbox)
            {
                project = p;
            }
        }

        if (!ModelState.IsValid)
        {
            Hook = hook;
            await LoadAsync();
            return Page();
        }

        if (hook is null)
        {
            hook = new WebhookToken { UserId = UserId, Token = Guid.NewGuid() };
            db.Webhooks.Add(hook);
        }

        hook.Name = Name.Trim().Length > 120 ? Name.Trim()[..120] : Name.Trim();
        hook.ProjectId = project;
        hook.IsActive = IsActive;
        await db.SaveChangesAsync();
        Flash(IsNew ? "Webhook angelegt. Kopiere jetzt die Adresse." : "Gespeichert.");
        return LocalRedirect($"/Account/Webhooks/Edit?id={hook.Id}");
    }

    /// <summary>A new secret: the old address stops working at once.</summary>
    public async Task<IActionResult> OnPostRenewAsync()
    {
        var hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == Id && w.UserId == UserId);
        if (hook is null)
        {
            return NotFound();
        }

        hook.Token = Guid.NewGuid();
        await db.SaveChangesAsync();
        Flash("Neue Adresse erzeugt. Die alte funktioniert nicht mehr.");
        return LocalRedirect($"/Account/Webhooks/Edit?id={hook.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var hook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == Id && w.UserId == UserId);
        if (hook is null)
        {
            return NotFound();
        }

        db.Webhooks.Remove(hook);
        await db.SaveChangesAsync();
        Flash($"Webhook „{hook.Name}“ gelöscht.");
        return LocalRedirect("/Account/Webhooks");
    }

    private async Task LoadAsync()
    {
        var settings = await accounts.SettingsAsync();
        BaseUrl = string.IsNullOrWhiteSpace(settings.PublicUrl) ? $"{Request.Scheme}://{Request.Host}" : settings.PublicUrl.TrimEnd('/');
        if (Hook is not null)
        {
            HookUrl = $"{BaseUrl}/api/v1/webhook/{Hook.Token}";
        }

        Projects = (await projects.ListAsync(UserId))
            .Select(x => new Choice(x.IsInbox ? string.Empty : x.Id.ToString(CultureInfo.InvariantCulture), x.IsInbox ? "Eingang" : x.TeamName is null ? x.Name : $"{x.Name} ({x.TeamName})", null, x.IsInbox ? null : x.Color))
            .ToList();
    }
}
