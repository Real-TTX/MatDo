using MatDo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatDo.Pages;

/// <summary>What every page behind the sign-in needs: the person, today in their zone, the messages after a redirect.</summary>
public abstract class AppPageModel : PageModel
{
    protected CurrentUser Me => HttpContext.RequestServices.GetRequiredService<CurrentUser>();

    protected long UserId => Me.RequiredId;

    public DateOnly Today => Me.Today;

    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["FlashError"] = message;

    /// <summary>Back to where the person came from, only to an address of this app.</summary>
    protected IActionResult Back(string? returnUrl, string fallback)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : LocalRedirect(fallback);

    /// <summary>The address of this page with its query (the return address of the forms on it).</summary>
    public string Here => Request.Path + Request.QueryString;
}
