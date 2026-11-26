using System.Net;
using MatDo.Data;
using MatDo.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatDo.Tests.Platform;

/// <summary>A headless install (<c>MATDO_ADMIN_PASSWORD</c>, as Mattiverse sets it): the first administrator is seeded, nobody has to register.</summary>
public sealed class HeadlessInstallTests(HeadlessFactory site) : IClassFixture<HeadlessFactory>
{
    [DbFact]
    public async Task The_administrator_is_seeded_and_the_sign_in_goes_straight_to_matidentity()
    {
        var browser = site.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await browser.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith(IdentityFactory.Authority + "/connect/authorize", login.Headers.Location!.OriginalString);

        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync();
        Assert.Equal(Database.SeededAdminEmail, admin.Email);
        Assert.Equal(UserRole.Admin, admin.Role);

        // The seeded administrator is not a person: the first one who comes through MatIdentity is still the administrator.
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        Assert.False(await accounts.AnyPersonAsync());

        // With a user there, another value changes nothing.
        Assert.False(await Database.SeedAdminAsync(accounts, "Another-Passw0rd-42", NullLogger.Instance));
        Assert.Equal(1, await db.Users.CountAsync());
    }
}
