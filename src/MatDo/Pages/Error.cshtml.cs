using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatDo.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public string Title { get; private set; } = "Etwas ist schiefgegangen";

    public string Text { get; private set; } = "Bitte versuche es gleich noch einmal.";

    public void OnGet(int? code) => Set(code);

    public void OnPost(int? code) => Set(code);

    private void Set(int? code)
    {
        (Title, Text) = code switch
        {
            404 => ("Nicht gefunden", "Diese Seite gibt es nicht, oder sie gehört jemand anderem."),
            403 => ("Kein Zugriff", "Dafür fehlt die Berechtigung."),
            400 => ("Ungültige Anfrage", "Bitte lade die Seite neu und versuche es noch einmal."),
            429 => ("Zu viele Anfragen", "Bitte einen Moment warten."),
            _ => (Title, Text),
        };
    }
}
