using System.Net;
using System.Security.Claims;
using System.Text.Json;
using MatDo.Data;
using MatDo.Services;
using MatPlatform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MatDo.Tests.Platform;

/// <summary>Without <c>MATIDENTITY_AUTHORITY</c> nothing changes: the local sign-in form, as before.</summary>
public sealed class LocalSignInTests(MatDoFactory site) : IClassFixture<MatDoFactory>
{
    [DbFact]
    public async Task The_sign_in_page_is_the_local_form_without_matidentity()
    {
        var response = await new Browser(site).GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.DoesNotContain("MatIdentity", html);
    }

    [DbFact]
    public async Task A_page_that_needs_a_sign_in_sends_to_the_local_form_and_there_is_no_oidc()
    {
        var browser = new Browser(site);
        var response = await browser.GetAsync("/Today");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Browser.PathOf(response.Headers.Location!));

        var callback = await browser.GetAsync("/signin-oidc");
        Assert.True(callback.StatusCode == HttpStatusCode.NotFound
                    || (callback.StatusCode == HttpStatusCode.Redirect && Browser.PathOf(callback.Headers.Location!).StartsWith("/Account/Login", StringComparison.Ordinal)));
    }

    [DbFact]
    public async Task The_local_sign_in_and_sign_out_work_as_before()
    {
        var browser = new Browser(site);
        var signedIn = await browser.SignInAsync("/Account/Login", MatDoFactory.AdminEmail, MatDoFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Today", Browser.PathOf(signedIn.Headers.Location!));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Today")).StatusCode);

        var signedOut = await browser.SignOutAsync();
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/Account/Login", Browser.PathOf(signedOut.Headers.Location!));

        var after = await browser.GetAsync("/Today");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/Account/Login", Browser.PathOf(after.Headers.Location!));
    }

    [DbFact]
    public async Task A_wrong_password_is_refused()
    {
        var response = await new Browser(site).SignInAsync("/Account/Login", MatDoFactory.AdminEmail, "wrong-password");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"login-form\"", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c>: the automatic sign-in at MatIdentity, the local way in with <c>?local=1</c>, the sign-out there.</summary>
public sealed class IdentitySignInTests(IdentityFactory site) : IClassFixture<IdentityFactory>
{
    [DbFact]
    public async Task The_sign_in_page_goes_straight_to_matidentity_with_code_and_pkce()
    {
        var response = await new Browser(site).GetAsync("/Account/Login?returnUrl=%2FInbox");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.StartsWith(IdentityFactory.Authority + "/connect/authorize", location.OriginalString);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("matdo", query["client_id"].ToString());
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrEmpty(query["code_challenge"].ToString()));
        Assert.EndsWith("/signin-oidc", query["redirect_uri"].ToString());
    }

    [DbFact]
    public async Task A_page_that_needs_a_sign_in_ends_at_matidentity()
    {
        var browser = new Browser(site);
        var first = await browser.GetAsync("/Today");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var login = Browser.PathOf(first.Headers.Location!);
        Assert.StartsWith("/Account/Login", login);

        var second = await browser.GetAsync(login);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.StartsWith(IdentityFactory.Authority, second.Headers.Location!.OriginalString);
    }

    [DbFact]
    public async Task The_local_form_stays_reachable_and_a_local_sign_out_does_not_sign_in_again()
    {
        var browser = new Browser(site);
        var page = await browser.GetAsync("/Account/Login?local=1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("id=\"lf-sso\"", html);

        var signedIn = await browser.SignInAsync("/Account/Login?local=1", MatDoFactory.AdminEmail, MatDoFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/Today", Browser.PathOf(signedIn.Headers.Location!));

        // A local session has no ID token: the sign-out stays here.
        var signedOut = await browser.SignOutAsync();
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/Account/Login?signedOut=1", Browser.PathOf(signedOut.Headers.Location!));
    }

    [DbTheory]
    [InlineData("/Account/Login?signedOut=1", "login-signed-out")]
    [InlineData("/Account/Login?ssoError=1", "login-sso-error")]
    [InlineData("/Account/Login?local=true", "lf-sso")]
    [InlineData("/Account/Login?local=1&denied=1", "login-denied")]
    public async Task After_the_sign_out_or_a_failure_there_is_no_automatic_sign_in(string path, string marker)
    {
        var response = await new Browser(site).GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"lf-sso\"", html);
        Assert.Contains($"id=\"{marker}\"", html);
    }

    [DbFact]
    public async Task A_new_person_is_made_an_account_with_its_role_and_the_sign_out_ends_the_session_there_too()
    {
        var (target, http) = await MatIdentity.SignInAsync(site, new MatIdentityPerson(
            "sub-new", "New.Person@example.com", "New Person", "newperson", "admin", "the-id-token", new ClaimsPrincipal()) { SessionId = "sid-new" }, "/Inbox");
        Assert.Equal("/Inbox", target);

        var browser = new Browser(site);
        browser.Take(http.Response);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Today")).StatusCode);

        var user = await MatIdentity.WithDbAsync(site, db => db.Users.AsNoTracking().SingleAsync(u => u.ExternalSubject == "sub-new"));
        Assert.Equal("new.person@example.com", user.Email);
        Assert.Equal("New Person", user.DisplayName);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(string.Empty, user.PasswordHash);
        Assert.True(await MatIdentity.WithDbAsync(site, db => db.Projects.AnyAsync(p => p.OwnerId == user.Id && p.IsInbox)));
        var session = await MatIdentity.WithDbAsync(site, db => db.Sessions.AsNoTracking().SingleAsync(s => s.UserId == user.Id));
        Assert.Equal("the-id-token", session.IdentityToken);
        Assert.Equal("sid-new", session.IdentitySessionId);

        var response = await browser.SignOutAsync();
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(IdentityFactory.Authority + "/connect/logout", location.OriginalString);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("the-id-token", query["id_token_hint"].ToString());
        Assert.EndsWith("/signout-callback-oidc", query["post_logout_redirect_uri"].ToString());
        Assert.True(await MatIdentity.WithDbAsync(site, db => db.Sessions.Where(s => s.UserId == user.Id).AllAsync(s => s.IsRevoked)));
    }

    [DbFact]
    public async Task The_role_claim_is_followed_at_every_sign_in_but_never_demotes_the_last_administrator()
    {
        MatIdentityPerson As(string role) => new("sub-role", "role.person@example.com", "Role Person", null, role, "t", new ClaimsPrincipal());

        await MatIdentity.SignInAsync(site, As("user"));
        Assert.Equal(UserRole.User, await RoleAsync("sub-role"));
        await MatIdentity.SignInAsync(site, As("admin"));
        Assert.Equal(UserRole.Admin, await RoleAsync("sub-role"));
        await MatIdentity.SignInAsync(site, As("something-else"));
        Assert.Equal(UserRole.Admin, await RoleAsync("sub-role"));
        await MatIdentity.SignInAsync(site, As("user"));
        Assert.Equal(UserRole.User, await RoleAsync("sub-role"));

        // The last active administrator: every other administrator is disabled, MatIdentity asks for "user", the role stays.
        await MatIdentity.SignInAsync(site, As("admin"));
        var others = await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive && u.ExternalSubject != "sub-role").Select(u => u.Id).ToListAsync());
        await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        try
        {
            await MatIdentity.SignInAsync(site, As("user"));
            Assert.Equal(UserRole.Admin, await RoleAsync("sub-role"));
        }
        finally
        {
            await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true)));
        }
    }

    [DbFact]
    public async Task A_person_without_an_address_gets_an_account_with_a_placeholder_and_a_disabled_one_is_refused()
    {
        var (target, _) = await MatIdentity.SignInAsync(site, new MatIdentityPerson("sub-noaddress", null, null, "nomail", null, "t", new ClaimsPrincipal()));
        Assert.Equal("/", target);
        var user = await MatIdentity.WithDbAsync(site, db => db.Users.AsNoTracking().SingleAsync(u => u.ExternalSubject == "sub-noaddress"));
        Assert.Equal(ExternalSignInService.PlaceholderEmail("sub-noaddress"), user.Email);
        Assert.Equal("nomail", user.DisplayName);
        Assert.Equal(UserRole.User, user.Role);

        await MatIdentity.WithDbAsync(site, db => db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));
        var (refused, http) = await MatIdentity.SignInAsync(site, new MatIdentityPerson("sub-noaddress", null, null, "nomail", null, "t", new ClaimsPrincipal()));
        Assert.Equal("/Account/Login?local=1&denied=1", refused);
        Assert.DoesNotContain(http.Response.Headers.SetCookie, c => c!.StartsWith(MatIdentity.SessionCookie + "=", StringComparison.Ordinal));
    }

    [DbFact]
    public async Task The_definition_names_matidentity_and_a_bad_token_is_401()
    {
        using var doc = JsonDocument.Parse(await site.CreateClient().GetStringAsync("/api"));
        Assert.Equal(IdentityFactory.Authority, doc.RootElement.GetProperty("auth").GetProperty("identity").GetString());

        var bearer = site.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bearer.GetAsync("/api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await site.Api().GetAsync("/api/state")).StatusCode);
    }

    [DbFact]
    public async Task The_webhook_stays_anonymous()
    {
        // An unknown token: the webhook itself answers (404), nobody is sent to MatIdentity.
        var response = await site.CreateClient().PostAsync("/api/v1/webhook/" + Guid.NewGuid(), new StringContent("Milch kaufen"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task<UserRole> RoleAsync(string subject)
        => MatIdentity.WithDbAsync(site, db => db.Users.AsNoTracking().Where(u => u.ExternalSubject == subject).Select(u => u.Role).SingleAsync());
}

/// <summary>MatIdentity is configured but down: the sign-in page falls back to the local form instead of an error.</summary>
public sealed class UnreachableIdentityTests(UnreachableIdentityFactory site) : IClassFixture<UnreachableIdentityFactory>
{
    [DbFact]
    public async Task The_sign_in_page_shows_the_local_form_with_a_message()
    {
        var response = await new Browser(site).GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"login-form\"", html);
        Assert.Contains("id=\"login-sso-error\"", html);
    }
}

/// <summary>What the callback of MatIdentity does for a person (no MatIdentity has to run), and the database of a test app.</summary>
internal static class MatIdentity
{
    public const string SessionCookie = "matdo.session";

    public static async Task<(string Target, DefaultHttpContext Http)> SignInAsync(MatDoFactory site, MatIdentityPerson person, string? returnUrl = null)
    {
        using var scope = site.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        return (await MatIdentitySignIn.CompleteSignInAsync(http, person, returnUrl), http);
    }

    public static async Task<T> WithDbAsync<T>(MatDoFactory site, Func<AppDbContext, Task<T>> work)
    {
        using var scope = site.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
