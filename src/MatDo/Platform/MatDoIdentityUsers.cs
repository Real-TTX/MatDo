using System.Globalization;
using MatDo.Services;
using MatPlatform;

namespace MatDo.Platform;

/// <summary>
/// MatDo's side of the sign-in through MatIdentity (MatPlatform <see cref="IMatIdentityUsers"/>): the person becomes an account of MatDo through
/// <see cref="ExternalSignInService"/> (by <c>sub</c>, else a new account; the claim <c>role</c> when it is there) and is then signed in with MatDo's own
/// cookie and session, like a local sign-in, keeping the ID token and the <c>sid</c> of MatIdentity with the session. An account with the same address is
/// not entered by the address: the answer is <see cref="MatIdentitySignInResult.LinkRequired"/>, and the sign-in page links it after its password
/// (<see cref="MatIdentityPendingLink"/>, <c>Pages/Account/Login</c>). When MatIdentity signs the person out (back-channel logout,
/// <see cref="IMatIdentitySessions"/>), their sessions here end too.
/// </summary>
public sealed class MatDoIdentityUsers(ExternalSignInService externalSignIn, SessionService sessions, ILogger<MatDoIdentityUsers> logger)
    : IMatIdentityUsers, IMatIdentitySessions
{
    public async Task<MatIdentitySignInResult> SignInAsync(MatIdentityPerson person, HttpContext http, CancellationToken ct)
    {
        var result = await externalSignIn.SignInExternalAsync(new ExternalIdentity(person.Subject, person.Email, person.Name, person.PreferredUserName, person.Role), ct);
        if (result.LinkUserId is { } linkUserId)
        {
            return MatIdentitySignInResult.LinkRequired(linkUserId.ToString(CultureInfo.InvariantCulture));
        }

        if (result.User is not { } user)
        {
            return MatIdentitySignInResult.Refused(result.Error ?? "refused");
        }

        await sessions.SignInAsync(http, user, remember: true, person.IdToken, person.SessionId);
        logger.LogInformation("User {UserId} signed in through MatIdentity", user.Id);
        return MatIdentitySignInResult.Success();
    }

    public async Task EndSessionsAsync(string? subject, string? sessionId, CancellationToken ct)
    {
        var ended = await sessions.EndIdentitySessionsAsync(subject, sessionId, ct);
        logger.LogInformation("MatIdentity signed out {Subject} (sid {SessionId}): {Count} session(s) ended", subject, sessionId, ended);
    }
}
