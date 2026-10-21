using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using MatDo.Data;
using MatDo.Services;
using MatPlatform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatDo.Tests.Platform;

/// <summary>
/// The address of an account proves nothing (anyone may register with any address), so an account with the address of a MatIdentity person is linked only
/// after its own password (MatPlatform <see cref="MatIdentityPendingLink"/>): the account-linking proof rule of docs/PLATFORM.md.
/// </summary>
public sealed class PendingLinkTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    private const string LinkPage = "/Account/Login?local=1&link=1";

    [DbFact]
    public async Task A_pre_claimed_address_is_not_entered_and_the_person_lands_on_the_local_sign_in()
    {
        var (claimer, email, _) = await CreateUserAsync();

        var (target, http) = await MatIdentity.SignInAsync(site, Person(email));

        Assert.Equal(LinkPage, target);
        // No session was started, the account is not linked and no second account was made.
        Assert.DoesNotContain(http.Response.Headers.SetCookie, c => c!.StartsWith(MatIdentity.SessionCookie + "=", StringComparison.Ordinal));
        Assert.Null((await UserAsync(claimer.Id)).ExternalSubject);
        Assert.False(await MatIdentity.WithDbAsync(site, db => db.Users.AnyAsync(u => u.ExternalSubject == "sub-" + email)));
        Assert.False(await MatIdentity.WithDbAsync(site, db => db.Sessions.AnyAsync(s => s.UserId == claimer.Id)));

        var browser = new Browser(site);
        browser.Take(http.Response);
        var page = await browser.GetAsync(LinkPage);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-link\"", html);
        Assert.Contains(email, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id=\"login-link-cancel\"", html);
        Assert.Contains("id=\"login-form\"", html);
    }

    [DbFact]
    public async Task The_sign_in_of_the_matched_account_links_it_with_the_role_the_id_token_and_the_sid()
    {
        var (owner, email, password) = await CreateUserAsync();
        var browser = await PendingBrowserAsync(email, role: "admin");

        var signedIn = await browser.SignInAsync(LinkPage, email, password);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Today", Browser.PathOf(signedIn.Headers.Location!));
        var user = await UserAsync(owner.Id);
        Assert.Equal("sub-" + email, user.ExternalSubject);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));
        Assert.True(browser.Has(MatIdentity.SessionCookie));
        var session = await MatIdentity.WithDbAsync(site, db => db.Sessions.AsNoTracking().SingleAsync(s => s.UserId == owner.Id));
        Assert.Equal("id-token-" + email, session.IdentityToken);
        Assert.Equal("sid-" + email, session.IdentitySessionId);

        // From now on MatIdentity finds the account by sub.
        var (target, _) = await MatIdentity.SignInAsync(site, Person(email));
        Assert.Equal("/", target);
    }

    [DbFact]
    public async Task Another_account_signing_in_links_nothing_and_ends_the_pending_link()
    {
        var (matched, email, _) = await CreateUserAsync();
        var (other, otherEmail, otherPassword) = await CreateUserAsync();
        var browser = await PendingBrowserAsync(email);

        var signedIn = await browser.SignInAsync(LinkPage, otherEmail, otherPassword);

        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
        Assert.Null((await UserAsync(other.Id)).ExternalSubject);
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));
        Assert.Null(await MatIdentity.WithDbAsync(site, db => db.Sessions.AsNoTracking().Where(s => s.UserId == other.Id).Select(s => s.IdentityToken).SingleAsync()));
    }

    [DbFact]
    public async Task A_wrong_password_keeps_the_link_pending()
    {
        var (matched, email, _) = await CreateUserAsync();
        var browser = await PendingBrowserAsync(email);

        var wrong = await browser.SignInAsync(LinkPage, email, "guessed-password");

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.True(browser.Has(MatIdentityPendingLink.CookieName));
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
    }

    [DbFact]
    public async Task Cancel_deletes_the_pending_link()
    {
        var (matched, email, password) = await CreateUserAsync();
        var browser = await PendingBrowserAsync(email);

        var html = await (await browser.GetAsync(LinkPage)).Content.ReadAsStringAsync();
        var cancel = await browser.PostAsync("/Account/Login?handler=CancelLink", new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Browser.Antiforgery(html),
        });

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal("/Account/Login?local=1", Browser.PathOf(cancel.Headers.Location!));
        Assert.False(browser.Has(MatIdentityPendingLink.CookieName));

        // The password afterwards is a plain local sign-in.
        await browser.SignInAsync("/Account/Login?local=1", email, password);
        Assert.Null((await UserAsync(matched.Id)).ExternalSubject);
    }

    [DbFact]
    public async Task An_expired_or_forged_pending_cookie_is_ignored()
    {
        var (matched, email, password) = await CreateUserAsync();
        var json = JsonSerializer.Serialize(
            new MatIdentityPendingLink("sub-" + email, email, "X", null, "admin", "t", matched.Id.ToString(CultureInfo.InvariantCulture)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expired = site.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(MatIdentityPendingLink.Purpose)
            .ToTimeLimitedDataProtector().Protect(json, DateTimeOffset.UtcNow.AddSeconds(-1));

        foreach (var value in new[] { expired, Uri.EscapeDataString(json), "CfDJ8forged" })
        {
            var browser = new Browser(site);
            browser.Set(MatIdentityPendingLink.CookieName, value);

            var html = await (await browser.GetAsync(LinkPage)).Content.ReadAsStringAsync();
            Assert.Contains("id=\"login-link-expired\"", html);
            Assert.DoesNotContain("id=\"login-link\"", html);

            await browser.SignInAsync(LinkPage, email, password);
            var user = await UserAsync(matched.Id);
            Assert.Null(user.ExternalSubject);
            Assert.Equal(UserRole.User, user.Role);
        }
    }

    [DbFact]
    public async Task A_disabled_match_or_one_linked_with_another_person_is_refused()
    {
        var (inactive, email, _) = await CreateUserAsync();
        await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => u.Id == inactive.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        Assert.Equal("/Account/Login?local=1&denied=1", (await MatIdentity.SignInAsync(site, Person(email))).Target);
        Assert.False(await MatIdentity.WithDbAsync(site, db => db.Users.AnyAsync(u => u.ExternalSubject == "sub-" + email)));

        var (linked, linkedEmail, _) = await CreateUserAsync();
        await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => u.Id == linked.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalSubject, "someone-else")));
        Assert.Equal("/Account/Login?local=1&denied=1", (await MatIdentity.SignInAsync(site, Person(linkedEmail))).Target);
        Assert.Equal("someone-else", (await UserAsync(linked.Id)).ExternalSubject);
    }

    private async Task<Browser> PendingBrowserAsync(string email, string? role = null)
    {
        var (target, http) = await MatIdentity.SignInAsync(site, Person(email, role));
        Assert.Equal(LinkPage, target);
        var browser = new Browser(site);
        browser.Take(http.Response);
        Assert.True(browser.Has(MatIdentityPendingLink.CookieName));
        return browser;
    }

    private static MatIdentityPerson Person(string email, string? role = null)
        => new("sub-" + email, email.ToUpperInvariant(), "Someone", "someone", role, "id-token-" + email, new ClaimsPrincipal()) { SessionId = "sid-" + email };

    /// <summary>A local account with a password and an address nobody else has.</summary>
    private async Task<(User User, string Email, string Password)> CreateUserAsync()
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        var email = name + "@example.com";
        const string password = "Correct-Horse-Battery";
        using var scope = site.Services.CreateScope();
        var (user, error) = await scope.ServiceProvider.GetRequiredService<AccountService>().RegisterAsync(email, name, password);
        Assert.True(user is not null, error?.Message);
        return (user!, email, password);
    }

    private Task<User> UserAsync(long id) => MatIdentity.WithDbAsync(site, db => db.Users.AsNoTracking().SingleAsync(u => u.Id == id));
}
