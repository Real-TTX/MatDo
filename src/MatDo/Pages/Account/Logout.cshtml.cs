using MatDo.Services;
using MatPlatform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatDo.Pages.Account;

/// <summary>
/// Signs out (a POST, so no prefetched link can do it). After a sign-in through MatIdentity the session there ends too (its end-session endpoint, then back
/// to <c>/signout-callback-oidc</c> and the sign-in page with <c>?signedOut=1</c>); with MatIdentity configured a local session lands on <c>?signedOut=1</c>
/// as well, so the sign-in page does not sign in again at once.
/// </summary>
public sealed class LogoutModel(SessionService sessions, MatPlatformSettings platform) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/Today");

    public async Task<IActionResult> OnPostAsync()
    {
        // Read before the local sign-out ends the session: only a sign-in through MatIdentity carries it.
        var identityToken = platform.IdentityEnabled ? await sessions.IdentityTokenAsync(HttpContext) : null;
        await sessions.SignOutAsync(HttpContext);

        if (identityToken is not null)
        {
            return MatIdentitySignIn.SignOutAtIdentity(HttpContext, identityToken);
        }

        return LocalRedirect(platform.IdentityEnabled ? "/Account/Login?signedOut=1" : "/Account/Login");
    }
}
