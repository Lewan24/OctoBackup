using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Modules.Crypto.Shared.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Modules.Administration.Api;
using Modules.Auth.Api;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Backup.Api;
using Modules.Backup.Application.Workers;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Hubs;
using Modules.Crypto.Api;
using Modules.Shared.Common;
using Server.Api.Security;
using System.Threading.RateLimiting;
using Server.Api.Configuration;
using Modules.Shared.Settings;

var builder = WebApplication.CreateBuilder(args);
var localSettingsCreated = DevelopmentSettings.Configure(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());
var settingsReloadLog = ReloadableAppSettings.Configure(builder.Configuration, builder.Environment.ContentRootPath);
// Plain HTTP is an explicit deployment choice for isolated test/private networks.
// Keep Secure cookies as the production default even though the API listener is HTTP.
var allowInsecureHttp = builder.Configuration.GetValue("Security:AllowInsecureHttp", false);
var cookieSecurePolicy = allowInsecureHttp ? CookieSecurePolicy.None
    : builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 128 * 1024);
var dataRoot = DevelopmentSettings.ResolveDataDirectory(builder.Configuration, builder.Environment.ContentRootPath);
var sshPins = builder.Configuration["OCTOBACKUP_SSH_HOST_KEYS"];
if (string.IsNullOrWhiteSpace(sshPins)) sshPins = builder.Configuration["Ssh:HostKeysFile"];
DbCommon.Configure(dataRoot, string.IsNullOrWhiteSpace(sshPins) ? null : Path.GetFullPath(sshPins, builder.Environment.ContentRootPath));
builder.Services.AddSingleton(_ => new FileApplicationSettings(builder.Configuration, dataRoot, _.GetRequiredService<ILogger<FileApplicationSettings>>()));
builder.Services.AddSingleton<IApplicationSettings>(provider => provider.GetRequiredService<FileApplicationSettings>());
builder.Services.AddDataProtection().SetApplicationName("OctoBackup")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(builder.Configuration["DataProtection:KeysDirectory"] ?? Path.Combine(dataRoot, "keys"), builder.Environment.ContentRootPath)));
var proxyEnabled = builder.Configuration.GetValue<bool>("ReverseProxy:Enabled");
if (proxyEnabled)
{
    var trusted = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    if (trusted.Length == 0) throw new InvalidOperationException("ReverseProxy:KnownProxies must list the trusted proxy IPs.");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in trusted) options.KnownProxies.Add(IPAddress.Parse(address));
    });
}
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddBackupModule();
builder.Services.AddAuthModule()
    .AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.SignIn.RequireConfirmedAccount = true;
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 12;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "OctoBackup.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = cookieSecurePolicy;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var manager = c.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await manager.GetUserAsync(c.Principal!);
        var stamp = c.Principal?.FindFirst("AspNet.Identity.SecurityStamp")?.Value;
        if (user is null || user.IsBlocked || !user.EmailConfirmed || user.SecurityStamp != stamp)
            c.RejectPrincipal();
    };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "OctoBackup.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = cookieSecurePolicy;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
        RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAdministrationModule();
builder.Services.AddCryptoModule();
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 16 * 1024);
builder.Services.AddHostedService<BackupWorker>();
builder.Services.AddHostedService<Modules.Backup.Application.Workers.RestoreTestWorker>();

var app = builder.Build();
settingsReloadLog.Logger = app.Logger;
if (localSettingsCreated)
    app.Logger.LogInformation("Created private development settings in {SettingsFile}. Initial administrator credentials are in that file; existing accounts are never replaced.",
        Path.Combine(builder.Environment.ContentRootPath, DevelopmentSettings.FileName));
if (proxyEnabled) app.UseForwardedHeaders();
app.UseExceptionHandler();
// TLS and HTTP-to-HTTPS redirects belong to the public reverse proxy.
// The private API accepts HTTP; production cookies default to Secure.
app.Use(async (c, next) =>
{
    c.Response.Headers.XContentTypeOptions = "nosniff";
    c.Response.Headers.XFrameOptions = "DENY";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    c.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    if (c.Request.Path.StartsWithSegments("/api"))
    {
        // Match antiforgery's required policy instead of making it override ours.
        c.Response.Headers.CacheControl = "no-cache, no-store";
        c.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
var serveFrontend = builder.Configuration.GetValue<bool>("Hosting:ServeFrontend");
if (serveFrontend) { app.UseDefaultFiles(); app.UseStaticFiles(); }
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (c, next) =>
{
    if (c.Request.Path.StartsWithSegments("/api") && c.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        var missingCookie = !c.Request.Cookies.ContainsKey("OctoBackup.Csrf");
        var insecureRequest = cookieSecurePolicy == CookieSecurePolicy.Always && !c.Request.IsHttps;
        var valid = false;
        if (!missingCookie && !insecureRequest)
        {
            try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); valid = true; }
            catch (AntiforgeryValidationException) { }
        }
        if (!valid)
        {
            c.Response.StatusCode = 400;
            await c.Response.WriteAsJsonAsync(new {
                code = missingCookie ? "csrf_cookie_missing" : insecureRequest ? "https_required" : "csrf_invalid",
                title = missingCookie
                    ? cookieSecurePolicy == CookieSecurePolicy.Always
                        ? "The browser did not send the security cookie. Open the application using HTTPS, allow cookies, and refresh the page."
                        : "The browser did not send the security cookie. Allow cookies and refresh the page."
                    : insecureRequest ? "Secure cookies require HTTPS. Open the public HTTPS URL and check the trusted reverse-proxy configuration."
                    : "Invalid security token. Refresh the page and try again." });
            return;
        }
    }
    await next();
});
app.MapGet("/api/auth/csrf", (HttpContext c, IAntiforgery a) =>
    cookieSecurePolicy == CookieSecurePolicy.Always && !c.Request.IsHttps
        ? Results.Problem(statusCode: 400, title: "Secure cookies require HTTPS. Open the public HTTPS URL and check the trusted reverse-proxy configuration.")
        : Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken })).AllowAnonymous();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapBackupModuleEndpoints();
app.MapAuthModuleEndpoints();
app.MapAdministrationModuleEndpoints();
app.MapApplicationSettings();
app.MapHub<BackupHub>(BackupHubHelper.HubUrl).RequireAuthorization();

app.MapFallback("/api/{**path}", () => Results.NotFound());
if (serveFrontend) app.MapFallbackToFile("index.html");
else app.MapGet("/", () => Results.Ok(new { service = "OctoBackup API", health = "/api/health" }));
DbCommon.CreateDbDirectoryIfNotExists();
using (var scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<ICryptoService>();
    await scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<Modules.Backup.Application.Services.BackupEncryptionService>().Initialize();
    await BootstrapAdmin.Initialize(scope.ServiceProvider, builder.Configuration);
}
app.Logger.LogInformation("OctoBackup API starting in {Environment}. Data: {DataPath}. Trusted proxy: {ProxyEnabled}. SPA hosting: {ServeFrontend}.",
    app.Environment.EnvironmentName, dataRoot, proxyEnabled, serveFrontend);
if (allowInsecureHttp)
    app.Logger.LogWarning("Insecure HTTP cookie mode is enabled. Use only on isolated test/private networks; disable Security:AllowInsecureHttp for public HTTPS deployments.");
app.Run();
public partial class Program;
