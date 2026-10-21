using System.Globalization;
using System.Reflection;
using System.Threading.RateLimiting;
using MatDo;
using MatDo.Api;
using MatDo.Data;
using MatDo.Localization;
using MatDo.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--healthcheck"))
{
    return await HealthCheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

// ---- the database (PostgreSQL): the connection string of appsettings.json fits the compose files, no variable is needed ----
var connection = builder.Configuration.GetConnectionString("Default") ?? "Host=db;Port=5432;Database=matdo;Username=matdo;Password=matdo";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connection));

// The keys that protect the cookie are in the volume: a session survives a restart of the container.
var dataDir = builder.Configuration["DataDirectory"] ?? "/data";
var keys = Path.Combine(dataDir, "keys");
Directory.CreateDirectory(keys);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keys)).SetApplicationName("MatDo");

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<Assets>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<MatDo.Services.Auth.AccountDataService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<QuickAddService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<QuickAddEditor>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddSingleton<LiveService>();
builder.Services.AddScoped<MatDo.Services.Notifications.PushService>();
builder.Services.AddHostedService<MatDo.Services.Notifications.NotificationService>();
builder.Services.AddHttpClient("push", c => c.Timeout = TimeSpan.FromSeconds(15));

// The interface speaks English and German (MatDo.Localization); every localizer reads the one catalog of JSON texts.
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, CatalogLocalizerFactory>();
builder.Services.AddSingleton(typeof(Microsoft.Extensions.Localization.IStringLocalizer<>), typeof(CatalogLocalizer<>));
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, Languages.UiCulture(Languages.Default));
    options.SupportedCultures = [CultureInfo.InvariantCulture];
    options.SupportedUICultures = [.. Languages.Codes.Select(Languages.UiCulture)];
    options.RequestCultureProviders = [new AccountLanguageProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "matdo.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.IsEssential = true;
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = SessionService.ValidateAsync;
        o.Events.OnRedirectToLogin = context =>
        {
            // a script or an API call gets a status, not the sign-in page
            if (context.Request.Path.StartsWithSegments("/api")
                || context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            }
            else
            {
                context.Response.Redirect(context.RedirectUri);
            }

            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Register");
    options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
}).AddMvcOptions(o =>
{
    o.ModelBinderProviders.Insert(0, new EmptyStringBinderProvider());
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

// The webhook: per token (or address) 60 tasks a minute; the sign-in form 10 attempts a minute per address.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("webhook", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Request.RouteValues["token"]?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("signin", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // behind a reverse proxy (Caddy, nginx, Traefik) the address and the scheme of the visitor are in the headers
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

await Database.MigrateWithRetryAsync(app);

app.UseForwardedHeaders();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}

app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseMiddleware<SecurityHeaders>();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // files are versioned with ?v= (asp-append-version): they can be kept for a long time
        if (ctx.Context.Request.Query.ContainsKey("v"))
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
    },
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseRequestLocalization();
app.UseAuthorization();

// The language switch (EN / DE): the cookie for the pages without an account, and the account of a signed-in person.
app.MapPost("/language", async (HttpContext http, CurrentUser me, AppDbContext db, Microsoft.AspNetCore.Antiforgery.IAntiforgery anti) =>
{
    await anti.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var code = Languages.Normalize(form["language"].ToString());
    Languages.SetCookie(http.Response, code, http.Request.IsHttps);
    if (me.Id is { } id)
    {
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Language, code));
        SessionService.Invalidate();
    }

    var back = form["returnUrl"].ToString();
    return Results.LocalRedirect(!string.IsNullOrEmpty(back) && back.StartsWith('/') && !back.StartsWith("//", StringComparison.Ordinal) ? back : "/");
}).AllowAnonymous();

app.MapGet("/health", async (AppDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok("ok") : Results.StatusCode(503)).AllowAnonymous();
app.MapWebhookApi();
app.MapAppApi();
app.MapPushApi();
app.MapRazorPages();

app.Run();
return 0;

/// <summary>The build: <c>0.1.42-20261019</c> (release), <c>nightly-42-20261019</c> (development) or <c>local-20261019</c>.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "local";
}

public partial class Program;
