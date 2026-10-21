using MatDo.Localization;
using MatDo.Controls;
using MatDo.Data;
using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Pages.Admin.Users;

/// <summary>An account: name, address, role, locked or not, a new password. The last active administrator cannot lose the role, be locked or be deleted.</summary>
public sealed class EditModel(AppDbContext db, AccountService accounts, PasswordService passwords, SessionService sessions, MatDo.Services.Auth.AccountDataService accountData) : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }

    [BindProperty] public string DisplayName { get; set; } = string.Empty;

    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty] public UserRole Role { get; set; } = UserRole.User;

    [BindProperty] public bool IsActive { get; set; } = true;

    [BindProperty] public string Password { get; set; } = string.Empty;

    public bool IsNew => Id is null;

    public bool IsSelf => Id == UserId;

    public List<Choice> Roles { get; } = [new("User", Text.T("Common.Role.User")), new("Admin", Text.T("Common.Role.Admin"))];

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
            var (created, error) = await accounts.RegisterAsync(Email, DisplayName, Password, Me.TimeZone, Role, Languages.Current);
            if (created is null)
            {
                ModelState.AddModelError(error!.Field, error.Message);
                return Page();
            }

            created.IsActive = IsActive;
            await db.SaveChangesAsync();
            Flash(Text.T("Users.Created", created.Email));
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
            ModelState.AddModelError(nameof(Email), Text.T("Account.Email.Invalid"));
        }
        else if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != user.Id))
        {
            ModelState.AddModelError(nameof(Email), Text.T("Account.Email.Taken"));
        }

        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ModelState.AddModelError(nameof(DisplayName), Text.T("Account.Name.Required"));
        }

        if ((Role != UserRole.Admin || !IsActive) && user.Role == UserRole.Admin && await LastAdminAsync(user.Id))
        {
            ModelState.AddModelError(nameof(Role), Text.T("Users.LastAdmin"));
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
        Flash(Text.T("Common.Saved"));
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
            FlashError(Text.T("Users.CannotDelete"));
            return LocalRedirect($"/Admin/Users/Edit?id={user.Id}");
        }

        // the account and its data go for good (GDPR Art. 17, the same as when the person deletes it): team projects stay with their teams
        await sessions.RevokeAllAsync(user.Id);
        await accountData.EraseAsync(user.Id);
        Flash(Text.T("Users.Deleted", DisplayName));
        return LocalRedirect("/Admin/Users");
    }

    private async Task<bool> LastAdminAsync(long id)
        => await db.Users.AnyAsync(u => u.Id == id && u.Role == UserRole.Admin && u.IsActive)
           && !await db.Users.AnyAsync(u => u.Id != id && u.Role == UserRole.Admin && u.IsActive);
}
