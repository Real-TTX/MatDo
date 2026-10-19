using System.Globalization;
using MatDo.Services;
using Microsoft.AspNetCore.Localization;

namespace MatDo.Localization;

/// <summary>
/// The languages of the interface: English and German. The language comes from the signed-in person (their account), else from the choice on the sign-in page
/// (a cookie), else from the browser (Accept-Language), else English. Only the UI culture changes: the request culture stays invariant, so numbers and dates in
/// forms and query strings are always read the same way (the app formats dates itself, see <see cref="Dates"/>).
/// </summary>
public static class Languages
{
    public const string Default = "en";

    public const string CookieName = ".MatDo.Language";

    /// <summary>Code, short label, name in its own language.</summary>
    public static readonly IReadOnlyList<(string Code, string Label, string Name)> Supported = [("en", "EN", "English"), ("de", "DE", "Deutsch")];

    public static IReadOnlyList<string> Codes { get; } = [.. Supported.Select(s => s.Code)];

    public static bool IsSupported(string? code) => code is not null && Codes.Contains(code, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? code)
    {
        var two = code is { Length: >= 2 } ? code[..2].ToLowerInvariant() : string.Empty;
        return IsSupported(two) ? two : Default;
    }

    /// <summary>The language of the current request ("en" or "de").</summary>
    public static string Current => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "iv" ? Default : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static bool IsGerman => Current == "de";

    public static CultureInfo UiCulture(string code) => CultureInfo.GetCultureInfo(Normalize(code));

    /// <summary>The choice of somebody who is not signed in (the sign-in page), kept for a year.</summary>
    public static void SetCookie(HttpResponse response, string code, bool secure)
        => response.Cookies.Append(CookieName, Normalize(code), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    /// <summary>Runs <paramref name="action"/> in another language (the answer of the webhook is in the language of the owner of the token).</summary>
    public static T In<T>(string code, Func<T> action)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = UiCulture(code);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }
}

/// <summary>Account first (the session put the person into the request), then the cookie of the sign-in page; Accept-Language and the default follow in the pipeline.</summary>
public sealed class AccountLanguageProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var code = (httpContext.Items[CurrentUser.ItemKey] as UserSnapshot)?.Language;
        if (!Languages.IsSupported(code))
        {
            code = httpContext.Request.Cookies[Languages.CookieName];
        }

        return Task.FromResult(Languages.IsSupported(code)
            ? new ProviderCultureResult(CultureInfo.InvariantCulture.Name, Languages.Normalize(code))
            : null);
    }
}
