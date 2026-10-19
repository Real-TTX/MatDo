using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatDo.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    /// <summary>The kind of the error, a part of the keys of its texts (Errors.NotFound.Title ...).</summary>
    public string Kind { get; private set; } = "General";

    public void OnGet(int? code) => Set(code);

    public void OnPost(int? code) => Set(code);

    private void Set(int? code) => Kind = code switch
    {
        404 => "NotFound",
        403 => "Forbidden",
        400 => "BadRequest",
        429 => "TooMany",
        _ => "General",
    };
}
