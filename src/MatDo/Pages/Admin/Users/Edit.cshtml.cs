using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Admin.Users;

/// <summary>An account: name, address, role, locked or not, a new password. The last active administrator cannot lose the role, be locked or be deleted.</summary>
public sealed class EditModel(AppDbContext db, AccountService accounts, PasswordService passwords, SessionService sessions) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty] public UserRole Role { get; set; } = UserRole.User;

    [BindProperty] public bool IsActive { get; set; } = true;

    [BindProperty] public string Password { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public bool IsSelf => Id == UserId;

    public List<Choice> Roles { get; } = [new("User", "Benutzer"), new("Admin", "Administrator")];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
            {
                return NotFound();
            }

            DisplayName = user.DisplayName;
            Email = user.Email;
            Role = user.Role;
            IsActive = user.IsActive;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Id is null)
        {
            var (created, error) = await accounts.RegisterAsync(Email, DisplayName, Password, Me.TimeZone, Role);
            if (created is null)
            {
                ModelState.AddModelError(error!.Contains("Passwort", StringComparison.Ordinal) ? nameof(Password) : nameof(Email), error);
                return Page();
            }

            created.IsActive = IsActive;
            await db.SaveChangesAsync();
            Flash($"Konto für {created.Email} angelegt.");
            return LocalRedirect("/Admin/Users");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        var email = AccountService.Normalize(Email);
        if (!AccountService.IsEmail(email))
        {
            ModelState.AddModelError(nameof(Email), "Bitte eine gültige E-Mail-Adresse eingeben.");
        }
        else if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != user.Id))
        {
            ModelState.AddModelError(nameof(Email), "Mit dieser Adresse gibt es schon ein Konto.");
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ModelState.AddModelError(nameof(DisplayName), "Bitte einen Namen eingeben.");
        }

        if ((Role != UserRole.Admin || !IsActive) && user.Role == UserRole.Admin && await LastAdminAsync(user.Id))
        {
            ModelState.AddModelError(nameof(Role), "Das ist der letzte aktive Administrator: er bleibt Administrator und aktiv.");
        }

        if (!string.IsNullOrEmpty(Password) && PasswordService.Check(Password, email) is { } problem)
        {
            ModelState.AddModelError(nameof(Password), problem);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = DisplayName.Trim();
        user.Email = email;
        user.Role = Enum.IsDefined(Role) ? Role : UserRole.User;
        user.IsActive = IsActive;
        if (!string.IsNullOrEmpty(Password))
        {
            user.PasswordHash = passwords.Hash(Password);
            user.LockoutUntil = null;
            user.FailedLogins = 0;
        }

        await db.SaveChangesAsync();
        if (!IsActive || !string.IsNullOrEmpty(Password))
        {
            await sessions.RevokeAllAsync(user.Id, IsSelf ? SessionService.CurrentSessionId(HttpContext) : null);
        }

        SessionService.Invalidate();
        Flash("Gespeichert.");
        return LocalRedirect("/Admin/Users");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        if (user.Id == UserId || (user.Role == UserRole.Admin && await LastAdminAsync(user.Id)))
        {
            FlashError("Das eigene Konto und den letzten Administrator kannst du hier nicht löschen.");
            return LocalRedirect($"/Admin/Users/Edit?id={user.Id}");
        }

        // the account goes softly (its address is freed); its sessions and webhooks end at once
        user.Email = $"deleted-{user.Id}-{Guid.NewGuid():N}@deleted.invalid";
        user.IsActive = false;
        db.Users.Remove(user);
        await db.Webhooks.Where(w => w.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.IsActive, false));
        db.TeamMembers.RemoveRange(await db.TeamMembers.Where(m => m.UserId == user.Id).ToListAsync());
        await db.SaveChangesAsync();
        await sessions.RevokeAllAsync(user.Id);
        Flash($"Konto von {DisplayName} gelöscht.");
        return LocalRedirect("/Admin/Users");
    }

    private async Task<bool> LastAdminAsync(long id)
        => await db.Users.AnyAsync(u => u.Id == id && u.Role == UserRole.Admin && u.IsActive)
           && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive);
}
