using System.Text.RegularExpressions;
using MatDo.Services;
using MatPlatform;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Npgsql;

// The factories hand their settings to Program through environment variables (Program reads them before the host is built),
// so the test classes run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MatDo.Tests.Platform;

/// <summary>
/// The whole app in memory (<see cref="WebApplicationFactory{TEntryPoint}"/>) on a database of its own on the server of <c>MATDO_TEST_DB</c> (the app creates
/// and migrates it at the start, the factory drops it) and an empty data folder, with the first account, the administrator (<see cref="AdminEmail"/>).
/// The background work (notifications, housekeeping) stays off. <see cref="ApiKey"/> is <c>MAT_API_KEY</c> unless <c>withKey</c> is false; with an
/// <c>authority</c> the sign-in through MatIdentity is on, with a fixed OpenID configuration so that no MatIdentity has to run.
/// </summary>
public class MatDoFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-key-0123456789";
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Correct-Horse-42";

    /// <summary>The key the imagined MatIdentity signs with (in the fixed OpenID configuration): for the logout tokens of the tests.</summary>
    public static readonly SigningCredentials IdentityCredentials = new(
        new RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048)) { KeyId = "test-key" }, SecurityAlgorithms.RsaSha256);

    private readonly string _data = Path.Combine(Path.GetTempPath(), "matdo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _database = "matdo_t_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string? _authority;
    private readonly bool _reachable;

    public MatDoFactory()
        : this(withKey: true, authority: null)
    {
    }

    protected MatDoFactory(bool withKey, string? authority, bool reachable = true)
    {
        _authority = authority;
        _reachable = reachable;
        Directory.CreateDirectory(_data);

        var connection = TestDatabase.AdminConnectionString is { } admin
            ? new NpgsqlConnectionStringBuilder(admin) { Database = _database }.ConnectionString
            : null;
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connection);
        Environment.SetEnvironmentVariable("DataDirectory", _data);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ApiKeyVariable, withKey ? ApiKey : null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.AuthorityVariable, authority);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientIdVariable, null);
        Environment.SetEnvironmentVariable(MatPlatformSettings.ClientSecretVariable, authority is null ? null : "test-secret");
        Environment.SetEnvironmentVariable(MatPlatformSettings.InternalUrlVariable, null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            // No notifications or housekeeping in the background: the tests look at the numbers they put in.
            foreach (var worker in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(worker);
            }
        });

        if (_authority is not null && _reachable)
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = _authority,
                AuthorizationEndpoint = _authority + "/connect/authorize",
                TokenEndpoint = _authority + "/connect/token",
                EndSessionEndpoint = _authority + "/connect/logout",
            };
            configuration.SigningKeys.Add(IdentityCredentials.Key);

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<OpenIdConnectOptions>(MatIdentitySignIn.Scheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
                services.PostConfigure<JwtBearerOptions>(MatApiAccess.BearerScheme, options =>
                {
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var (user, error) = scope.ServiceProvider.GetRequiredService<AccountService>()
            .RegisterAsync(AdminEmail, "Administrator", AdminPassword).GetAwaiter().GetResult();
        Assert.True(user is not null, error?.Message);
        return host;
    }

    /// <summary>A client with the key (no redirects followed).</summary>
    public HttpClient Api()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        NpgsqlConnection.ClearAllPools();

        if (TestDatabase.AdminConnectionString is { } admin)
        {
            try
            {
                using var connection = new NpgsqlConnection(admin);
                connection.Open();
                using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
                command.ExecuteNonQuery();
            }
            catch (NpgsqlException)
            {
                // the server is gone already
            }
        }

        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a key file may still be open; the temp folder is cleaned by the system
        }
    }
}

/// <summary>Without <c>MAT_API_KEY</c>: the protected endpoints answer 401 to everyone.</summary>
public sealed class NoKeyFactory() : MatDoFactory(withKey: false, authority: null);

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> (a MatIdentity that does not need to run).</summary>
public sealed class IdentityFactory() : MatDoFactory(withKey: true, authority: Authority)
{
    public const string Authority = "http://identity.test";
}

/// <summary>With <c>MATIDENTITY_AUTHORITY</c> pointing at an address where nothing answers (MatIdentity is down).</summary>
public sealed class UnreachableIdentityFactory() : MatDoFactory(withKey: true, authority: "http://127.0.0.1:9", reachable: false);

/// <summary>
/// Tests that start the app need PostgreSQL: <c>MATDO_TEST_DB</c> is an admin connection string, e.g. <c>Host=localhost;Port=5432;Username=postgres;Password=x</c>
/// (every factory creates and drops a database of its own there). Without it they are skipped, and the rest of the suite runs alone.
/// </summary>
public static class TestDatabase
{
    public const string Variable = "MATDO_TEST_DB";

    public const string SkipReason = Variable + " is not set (needs a PostgreSQL server).";

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } value ? value : null;
}

/// <summary>A test that needs PostgreSQL (<see cref="TestDatabase"/>); skipped without it.</summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (TestDatabase.AdminConnectionString is null)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

/// <summary>A theory that needs PostgreSQL (<see cref="TestDatabase"/>); skipped without it.</summary>
public sealed class DbTheoryAttribute : TheoryAttribute
{
    public DbTheoryAttribute()
    {
        if (TestDatabase.AdminConnectionString is null)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

/// <summary>Where the repository is, for the files the tests read (the sprite).</summary>
public static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MatDo.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("MatDo.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>MatDo's copy of the MatUi sprite: the icons a consumer such as Mattiverse can draw.</summary>
    public static string Sprite => Path.Combine(Root, "src", "MatDo", "wwwroot", "matui", "matui-icons.svg");
}

/// <summary>
/// A browser with its own cookie jar (no redirects followed), so a cookie made outside the app (a sign-in through MatIdentity, the pending link) can be put in.
/// Each browser comes from an address of its own (<c>X-Forwarded-For</c>, which MatDo reads), so the rate limit of the sign-in form counts it alone.
/// </summary>
public sealed partial class Browser(MatDoFactory site)
{
    private static int _next;
    private readonly HttpClient _client = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    private readonly Dictionary<string, string> _cookies = [];
    private readonly string _address = NextAddress();

    public bool Has(string name) => _cookies.ContainsKey(name);

    public void Set(string name, string value) => _cookies[name] = value;

    public void Take(Microsoft.AspNetCore.Http.HttpResponse response) => Take(response.Headers.SetCookie.Select(c => c ?? string.Empty));

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

    public Task<HttpResponseMessage> PostAsync(string path, Dictionary<string, string> form)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) });

    /// <summary>The local sign-in form on <paramref name="path"/>, filled in and sent.</summary>
    public async Task<HttpResponseMessage> SignInAsync(string path, string email, string password)
    {
        var html = await (await GetAsync(path)).Content.ReadAsStringAsync();
        return await PostAsync(path, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Antiforgery(html),
            ["Email"] = email,
            ["Password"] = password,
            ["Remember"] = "true",
        });
    }

    /// <summary>Signs out with the form of a page that was just loaded.</summary>
    public async Task<HttpResponseMessage> SignOutAsync(string page = "/Today")
    {
        var html = await (await GetAsync(page)).Content.ReadAsStringAsync();
        return await PostAsync("/Account/Logout", new Dictionary<string, string> { ["__RequestVerificationToken"] = Antiforgery(html) });
    }

    public static string Antiforgery(string html)
    {
        var token = AntiforgeryRegex().Match(html).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "No antiforgery token");
        return token;
    }

    public static string PathOf(Uri location) => location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;

    private static string NextAddress()
    {
        var n = Interlocked.Increment(ref _next);
        return $"10.9.{n / 250 % 250}.{n % 250 + 1}";
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""")]
    private static partial Regex AntiforgeryRegex();

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.Add("X-Forwarded-For", _address);
        if (_cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
        }

        var response = await _client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            Take(values);
        }

        return response;
    }

    private void Take(IEnumerable<string> setCookies)
    {
        foreach (var cookie in SetCookieHeaderValue.ParseList(setCookies.ToList()))
        {
            var name = cookie.Name.Value!;
            if (string.IsNullOrEmpty(cookie.Value.Value) || cookie.Expires < DateTimeOffset.UtcNow)
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = cookie.Value.Value!;
            }
        }
    }
}
