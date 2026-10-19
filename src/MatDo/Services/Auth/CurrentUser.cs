using MatDo.Data;

namespace MatDo.Services;

/// <summary>What the pages need to know about the signed-in person, read once per request from the session (no query of its own).</summary>
public sealed record UserSnapshot(long Id, string Email, string DisplayName, UserRole Role, string Theme, string Scheme, string TimeZone, string Language);

/// <summary>The signed-in user of the request. Background work and the webhook have none (<see cref="Id"/> is <c>null</c>).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public const string ItemKey = "MatDo.User";

    /// <summary>Set by the webhook: the person the token belongs to (the audit columns name them).</summary>
    public long? ActingUserId { get; set; }

    public UserSnapshot? Snapshot => accessor.HttpContext?.Items[ItemKey] as UserSnapshot;

    public bool IsAuthenticated => Snapshot is not null;

    public long? Id => Snapshot?.Id ?? ActingUserId;

    /// <summary>The id of the signed-in person; throws when there is none (only call it behind the sign-in).</summary>
    public long RequiredId => Snapshot?.Id ?? throw new InvalidOperationException("No signed-in user.");

    public bool IsAdmin => Snapshot?.Role == UserRole.Admin;

    public string DisplayName => Snapshot?.DisplayName ?? string.Empty;

    public string TimeZone => Snapshot?.TimeZone ?? "Europe/Berlin";

    public DateOnly Today => Dates.Today(TimeZone);

    public string RoleName => Localization.Text.T(Snapshot?.Role == UserRole.Admin ? "Common.Role.Admin" : "Common.Role.User");
}
