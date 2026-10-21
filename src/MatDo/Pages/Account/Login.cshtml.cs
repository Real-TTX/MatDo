using System.Globalization;
using MatDo.Localization;
using MatDo.Services;
using MatPlatform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace MatDo.Pages.Account;

/// <summary>
/// The local sign-in with address and password. With MatIdentity configured (<c>MATIDENTITY_AUTHORITY</c>) a visit goes straight on to MatIdentity; the local
/// form stays reachable with <c>?local=1</c> (a way in for the administrator when MatIdentity is down), and <c>?signedOut=1</c> / <c>?ssoError=1</c> show it
/// too instead of signing in again at once (<c>?denied=1</c>: MatIdentity signed the person in, MatDo refused the account).
/// After a sign-in through MatIdentity whose address matches an account that is not linked yet, the page shows <c>?local=1&amp;link=1</c>: a successful
/// sign-in of exactly that account links it (<see cref="MatIdentityPendingLink"/>, <see cref="ExternalSignInService.LinkExternalAsync"/>); another account signs
/// in without linking anything; Cancel deletes the pending link.
/// </summary>
[EnableRateLimiting("signin")]
public sealed class LoginModel(AccountService accounts, SessionService sessions, ExternalSignInService externalSignIn, MatPlatformSettings platform, ILogger<LoginModel> logger)
    : PageModel
{
    [BindProperty] public string Email { get; set; } = string.Empty;

    [BindProperty] public string Password { get; set; } = string.Empty;

    [BindProperty] public bool Remember { get; set; } = true;

    [BindProperty(SupportsGet = true)] public string ReturnUrl { get; set; } = string.Empty;

    public bool CanRegister { get; private set; }

    /// <summary>The sign-in through MatIdentity is configured.</summary>
    public bool IdentityEnabled => platform.IdentityEnabled;

    /// <summary><c>?signedOut=1</c>: back from the sign-out, no automatic sign-in.</summary>
    public bool SignedOut { get; private set; }

    /// <summary><c>?ssoError=1</c>: the sign-in at MatIdentity failed or MatIdentity cannot be reached.</summary>
    public bool SsoError { get; private set; }

    /// <summary><c>?denied=1</c>: MatIdentity signed the person in, but MatDo refused the account.</summary>
    public bool Denied { get; private set; }

    /// <summary>The pending link to an account of this address, when MatIdentity sent the person here to prove it (<c>?link=1</c>).</summary>
    public MatIdentityPendingLink? PendingLink { get; private set; }

    /// <summary><c>?link=1</c> without a pending link: it expired (10 minutes) or was cancelled.</summary>
    public bool LinkExpired { get; private set; }

    /// <summary>The address that starts the sign-in at MatIdentity and comes back to <see cref="ReturnUrl"/>.</summary>
    public string IdentitySignInUrl => string.IsNullOrEmpty(ReturnUrl) ? "/Account/Login" : "/Account/Login?returnUrl=" + Uri.EscapeDataString(ReturnUrl);

    private bool Local { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Prepare();

        // Auto login: straight on to MatIdentity, no local form in the way (also for the very first person: MatIdentity makes the first account).
        if (IdentityEnabled && !Local && !SignedOut && !SsoError)
        {
            try
            {
                await HttpContext.ChallengeAsync(MatIdentitySignIn.Scheme, new AuthenticationProperties { RedirectUri = Target() });
                return new EmptyResult();
            }
            catch (InvalidOperationException ex)
            {
                // MatIdentity cannot be reached (its configuration cannot be read): the local form is the way in.
                logger.LogWarning(ex, "MatIdentity is not reachable, showing the local sign-in");
                SsoError = true;
            }
        }

        if (!await accounts.AnyUserAsync())
        {
            return LocalRedirect("/Account/Register");
        }

        CanRegister = (await accounts.SettingsAsync()).RegistrationOpen;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Prepare();
        CanRegister = (await accounts.SettingsAsync()).RegistrationOpen;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ModelState.AddModelError(nameof(Email), Text.T("Account.Login.BothRequired"));
            return Page();
        }

        var result = await accounts.SignInAsync(Email, Password);
        if (result.User is not { } user)
        {
            ModelState.AddModelError(nameof(Email), result.Error!);
            return Page();
        }

        // A pending link of MatIdentity is only ever for the account that matched; it is deleted either way.
        string? identityToken = null;
        string? identitySessionId = null;
        if (MatIdentityPendingLink.TakeFor(HttpContext, user.Id.ToString(CultureInfo.InvariantCulture)) is { } pending)
        {
            var link = await externalSignIn.LinkExternalAsync(
                user.Id, new ExternalIdentity(pending.Subject, pending.Email, pending.Name, pending.PreferredUserName, pending.Role), ct);
            if (link.User is { } linked)
            {
                user = linked;
                identityToken = pending.IdToken;
                identitySessionId = pending.SessionId;
                TempData["Flash"] = Text.T("Account.Login.Linked");
            }
            else
            {
                logger.LogWarning("User {UserId} signed in, but was not linked with MatIdentity: {Error}", user.Id, link.Error);
            }
        }

        await sessions.SignInAsync(HttpContext, user, Remember, identityToken, identitySessionId);
        return LocalRedirect(Target());
    }

    /// <summary>Cancel on <c>?link=1</c>: the account is not linked, the pending link is deleted.</summary>
    public IActionResult OnPostCancelLink()
    {
        MatIdentityPendingLink.Clear(HttpContext);
        return LocalRedirect(string.IsNullOrEmpty(ReturnUrl) ? "/Account/Login?local=1" : "/Account/Login?local=1&returnUrl=" + Uri.EscapeDataString(ReturnUrl));
    }

    private void Prepare()
    {
        Local = Flag("local");
        SignedOut = Flag("signedOut");
        SsoError = Flag("ssoError");
        Denied = Flag("denied");
        PendingLink = MatIdentityPendingLink.Peek(HttpContext);
        LinkExpired = PendingLink is null && Flag("link");
    }

    /// <summary>A flag in the query: <c>1</c> or <c>true</c>.</summary>
    private bool Flag(string name) => Request.Query[name].ToString() is "1" or "true" or "True";

    /// <summary>Only a local address, else the day's tasks.</summary>
    private string Target() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Today";
}
