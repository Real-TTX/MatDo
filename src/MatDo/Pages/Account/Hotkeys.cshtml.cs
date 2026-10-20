namespace MatDo.Pages.Account;

/// <summary>One key of the app and what it does (the page lists them; the scripts are where they work).</summary>
public sealed record Hotkey(string[] Keys, string What);

/// <summary>The keys of the app, in one place instead of hints on every page.</summary>
public sealed class HotkeysModel : AppPageModel
{
    public void OnGet()
    {
    }
}
