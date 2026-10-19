using System.Net.Mail;
using MatDo.Data;
using Microsoft.EntityFrameworkCore;

namespace MatDo.Services;

public sealed record SignInResult(User? User, string? Error);

/// <summary>Accounts: registering (the first account is the administrator), signing in with a lockout after failed attempts, and what a new account gets (its inbox, the keywords).</summary>
public sealed class AccountService(AppDbContext db, PasswordService passwords)
{
    private const int MaxFailures = 8;

    /// <summary>The keywords a new account starts with; the person changes them in the settings.</summary>
    public static readonly IReadOnlyList<(KeywordKind Kind, string Word)> DefaultKeywords =
    [
        (KeywordKind.Project, "Projekt"),
        (KeywordKind.Project, "Project"),
        (KeywordKind.Label, "Label"),
        (KeywordKind.Label, "Etikett"),
        (KeywordKind.Priority, "Priorität"),
        (KeywordKind.Priority, "Priority"),
        (KeywordKind.Section, "Abschnitt"),
        (KeywordKind.Deadline, "Deadline"),
        (KeywordKind.Deadline, "Frist"),
        (KeywordKind.Description, "Beschreibung"),
        (KeywordKind.Description, "Notiz"),
    ];

    public static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !email.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email.Trim();
    }

    public async Task<bool> AnyUserAsync() => await db.Users.IgnoreQueryFilters().AnyAsync();

    public async Task<SystemSetting> SettingsAsync()
    {
        var s = await db.SystemSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null)
        {
            s = new SystemSetting { Id = 1 };
            db.SystemSettings.Add(s);
            await db.SaveChangesAsync();
        }

        return s;
    }

    /// <summary>Creates an account with its inbox and the default keywords. The very first account is an administrator.</summary>
    public async Task<(User? User, string? Error)> RegisterAsync(string email, string displayName, string password, string? timeZone = null, UserRole? role = null)
    {
        email = Normalize(email);
        if (!IsEmail(email))
        {
            return (null, "Bitte eine gültige E-Mail-Adresse eingeben.");
        }

        if (PasswordService.Check(password, email) is { } problem)
        {
            return (null, problem);
        }

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
        {
            return (null, "Mit dieser Adresse gibt es schon ein Konto.");
        }

        var first = !await AnyUserAsync();
        var user = new User
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
            PasswordHash = passwords.Hash(password),
            Role = role ?? (first ? UserRole.Admin : UserRole.User),
            TimeZone = Dates.IsZone(timeZone) ? timeZone! : "Europe/Berlin",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await ProvisionAsync(user);
        return (user, null);
    }

    /// <summary>The inbox and the keywords of a new account.</summary>
    public async Task ProvisionAsync(User user)
    {
        if (!await db.Projects.AnyAsync(p => p.OwnerId == user.Id && p.IsInbox))
        {
            db.Projects.Add(new Project { OwnerId = user.Id, Name = "Eingang", IsInbox = true, Color = "#246fe0", CreateUserId = user.Id });
        }

        if (!await db.Keywords.AnyAsync(k => k.UserId == user.Id))
        {
            foreach (var (kind, word) in DefaultKeywords)
            {
                db.Keywords.Add(new UserKeyword { UserId = user.Id, Kind = kind, Word = word, CreateUserId = user.Id });
            }
        }

        user.CreateUserId ??= user.Id;
        await db.SaveChangesAsync();
    }

    public async Task<SignInResult> SignInAsync(string email, string password)
    {
        email = Normalize(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            passwords.SpendTime(password);
            return new SignInResult(null, "E-Mail-Adresse oder Passwort stimmen nicht.");
        }

        var now = DateTime.UtcNow;
        if (user.LockoutUntil is { } until && until > now)
        {
            return new SignInResult(null, "Zu viele Fehlversuche. Bitte in ein paar Minuten noch einmal versuchen.");
        }

        if (!passwords.Verify(user.PasswordHash, password))
        {
            user.FailedLogins++;
            if (user.FailedLogins >= MaxFailures)
            {
                user.LockoutUntil = now.AddMinutes(10);
                user.FailedLogins = 0;
            }

            await db.SaveChangesAsync();
            return new SignInResult(null, "E-Mail-Adresse oder Passwort stimmen nicht.");
        }

        if (!user.IsActive)
        {
            return new SignInResult(null, "Dieses Konto ist gesperrt.");
        }

        user.FailedLogins = 0;
        user.LockoutUntil = null;
        user.LastLoginDate = now;
        await db.SaveChangesAsync();
        return new SignInResult(user, null);
    }
}
