namespace MatDo.Data;

/// <summary>What every record has: an Id (BIGINT) and who created and changed it, and when (the columns are filled by <see cref="AppDbContext"/>).</summary>
public abstract class Entity
{
    public long Id { get; set; }

    public DateTime CreateDate { get; set; }

    public long? CreateUserId { get; set; }

    public DateTime UpdateDate { get; set; }

    public long? UpdateUserId { get; set; }
}

/// <summary>A record that supports soft delete: 0 = deleted, 1 = created, 2 = updated. A deleted record is hidden by a query filter.</summary>
public abstract class SoftEntity : Entity
{
    public const short Deleted = 0;
    public const short Created = 1;
    public const short Updated = 2;

    public short UpdateState { get; set; } = Created;
}

public enum UserRole
{
    Admin = 1,
    User = 2,
}

public enum TeamRole
{
    Owner = 1,
    Member = 2,
}

/// <summary>What a keyword of the quick add introduces ("Projekt EIMER", "Label Einkauf", "Priorität 1" ...).</summary>
public enum KeywordKind
{
    Project = 1,
    Label = 2,
    Priority = 3,
    Description = 4,
    Deadline = 5,
    Section = 6,
}

/// <summary>Where a task came from.</summary>
public enum TaskSource
{
    App = 0,
    Webhook = 1,

    /// <summary>Noted without a network and sent when it came back (offline.js).</summary>
    Offline = 2,
}

public sealed class User : SoftEntity
{
    /// <summary>The address to sign in with; unique, stored in lower case.</summary>
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    /// <summary>The IANA time zone "today" and every due date are computed in.</summary>
    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>The language of the interface: en or de; empty = the language of the browser.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>A project named by the quick add or the webhook that does not exist is created instead of being ignored.</summary>
    public bool AutoCreateProjects { get; set; }

    /// <summary>A label named by the quick add that does not exist is created (the default, like Todoist).</summary>
    public bool AutoCreateLabels { get; set; } = true;

    public int FailedLogins { get; set; }

    public DateTime? LockoutUntil { get; set; }

    public DateTime? LastLoginDate { get; set; }

    /// <summary>system, light or dark.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>standard, paper, contrast or oled.</summary>
    public string Scheme { get; set; } = "standard";

    /// <summary>Minutes before the time of a task its reminder is sent (0 = at the time); null = no reminders.</summary>
    public int? ReminderMinutes { get; set; } = 0;

    /// <summary>The time of the daily overview (the tasks of today and the overdue ones); null = none.</summary>
    public TimeOnly? DigestTime { get; set; } = new(8, 0);

    /// <summary>The day the last overview was sent (in the zone of the person), so it goes once a day.</summary>
    public DateOnly? LastDigestDate { get; set; }

    /// <summary>The person of MatIdentity (the claim <c>sub</c>) this account is linked with; null = a local account only.</summary>
    public string? ExternalSubject { get; set; }
}

/// <summary>A browser or an installed app that receives notifications (Web Push): the subscription the browser handed out. Deleted when the push service says it is gone.</summary>
public sealed class PushDevice : Entity
{
    public long UserId { get; set; }

    /// <summary>The address of the push service for this browser; unique.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The public key (P-256) and the secret of the browser, both base64url: the message is encrypted for them.</summary>
    public string P256dh { get; set; } = string.Empty;

    public string Auth { get; set; } = string.Empty;

    /// <summary>What the device is, for the list in the settings ("iPhone · Safari").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The language of the browser when it subscribed (for a person whose language is "automatic").</summary>
    public string Language { get; set; } = string.Empty;

    public DateTime? LastSentDate { get; set; }
}

/// <summary>A signed-in browser. The cookie carries the <see cref="Token"/>; the session lives in the database.</summary>
public sealed class UserSession : Entity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime LastSeenDate { get; set; }

    public DateTime ExpiresDate { get; set; }

    public bool IsRevoked { get; set; }

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>The ID token of a sign-in through MatIdentity (for the sign-out there); null for a local sign-in. A secret: never exported.</summary>
    public string? IdentityToken { get; set; }

    /// <summary>The session of MatIdentity (the claim <c>sid</c>) this session started with: the back-channel logout ends it by this.</summary>
    public string? IdentitySessionId { get; set; }
}

/// <summary>A word of the quick add that introduces a value: <c>Projekt</c> in "Projekt EIMER".</summary>
public sealed class UserKeyword : SoftEntity
{
    public long UserId { get; set; }

    public KeywordKind Kind { get; set; }

    public string Word { get; set; } = string.Empty;
}

/// <summary>A group of people that share projects.</summary>
public sealed class Team : SoftEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Color { get; set; } = "#d03d33";

    public List<TeamMember> Members { get; set; } = [];
}

public sealed class TeamMember : Entity
{
    public long TeamId { get; set; }

    public Team? Team { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    public TeamRole Role { get; set; } = TeamRole.Member;
}

/// <summary>A list of tasks. Personal (<see cref="TeamId"/> is null: only the owner sees it) or of a team (every member sees and changes it).</summary>
public sealed class Project : SoftEntity
{
    public long OwnerId { get; set; }

    public User? Owner { get; set; }

    public long? TeamId { get; set; }

    public Team? Team { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#808080";

    /// <summary>The inbox of its owner: every person has exactly one, it cannot be deleted or renamed.</summary>
    public bool IsInbox { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }
}

public sealed class Section : SoftEntity
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

/// <summary>
/// A task. The due date is a date of the calendar (and an optional time of day) in the time zone of the person, like Todoist's "floating" dates:
/// "tomorrow at 9" stays 9 o'clock wherever the person is.
/// </summary>
public sealed class TaskItem : SoftEntity
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public long? SectionId { get; set; }

    public Section? Section { get; set; }

    /// <summary>The task this one is a sub-task of.</summary>
    public long? ParentId { get; set; }

    public TaskItem? Parent { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>1 (highest, red) to 4 (none), as in Todoist's p1 .. p4.</summary>
    public int Priority { get; set; } = 4;

    public DateOnly? DueDate { get; set; }

    public TimeOnly? DueTime { get; set; }

    /// <summary>The rule of a repeating task (<see cref="Services.Recurrence"/>), and the words it was given in.</summary>
    public string? Recurrence { get; set; }

    public string? RecurrenceText { get; set; }

    public DateOnly? DeadlineDate { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime? CompletedDate { get; set; }

    public long? AssigneeId { get; set; }

    public User? Assignee { get; set; }

    public int SortOrder { get; set; }

    public TaskSource Source { get; set; } = TaskSource.App;

    /// <summary>The moment (UTC) of the due date and time the reminder was sent for; a new date or time is a new moment and so a new reminder.</summary>
    public DateTime? RemindedFor { get; set; }

    public List<TaskLabel> Labels { get; set; } = [];
}

public sealed class Label : SoftEntity
{
    public long OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#808080";

    public bool IsFavorite { get; set; }

    public int SortOrder { get; set; }
}

public sealed class TaskLabel : Entity
{
    public long TaskId { get; set; }

    public TaskItem? Task { get; set; }

    public long LabelId { get; set; }

    public Label? Label { get; set; }
}

public sealed class TaskComment : SoftEntity
{
    public long TaskId { get; set; }

    public long AuthorId { get; set; }

    public User? Author { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>An address that creates tasks (Apple Shortcuts, Zapier, curl): <c>POST /api/v1/webhook/{Token}</c>. The token is the secret.</summary>
public sealed class WebhookToken : SoftEntity
{
    public long UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid Token { get; set; } = Guid.NewGuid();

    /// <summary>Where a task goes when its text names no project; null = the inbox.</summary>
    public long? ProjectId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastUsedDate { get; set; }

    public int UseCount { get; set; }
}

/// <summary>The settings of the whole installation (one row, Id 1).</summary>
public sealed class SystemSetting : Entity
{
    /// <summary>Whether anyone may create an account. Closed until an administrator opens it (Administration → System): the first account of an installation can always
    /// sign up (it becomes the administrator), after it the administrators create the accounts.</summary>
    public bool RegistrationOpen { get; set; }

    /// <summary>The address the app is reached at from outside (for the webhook addresses shown to a person); empty = the address of the request.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>The key pair (P-256, base64url) the app signs its notifications with (VAPID); made at the first need, kept for good: a new pair ends every subscription.</summary>
    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }
}
