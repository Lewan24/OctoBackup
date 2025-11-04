using Blazored.LocalStorage;
using Client.UI;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Modules.Administration.Api;
using Modules.Auth.Api;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Backup.Api;
using Modules.Backup.Application.Workers;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Hubs;
using Modules.BackupsTests.Api;
using Modules.Crypto.Api;
using Modules.Shared.Common;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Scalar.AspNetCore;
using Server.Api.Common;

const string defaultCorsPolicyName = "Default";

var builder = WebApplication.CreateBuilder(args);

IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", false, true)
    .Build();

builder.Configuration.AddConfiguration(config);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization(options => options.SerializeAllClaims = true);

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddEndpointsApiExplorer();

if (builder.Environment.IsDevelopment())
    builder.Services.AddOpenApi();

builder.Services.AddBackupModule();
builder.Services.AddBackupsTestsModule();

builder.Services.AddLogging(logging =>
{
    logging.AddConfiguration(config.GetSection("Logging"));

    var seqConfig = config.GetSection("SeqConfiguration").Get<SeqConfiguration>();

    if (seqConfig is not null &&
        !string.IsNullOrWhiteSpace(seqConfig.ApiKey) &&
        !string.IsNullOrWhiteSpace(seqConfig.Host))
        logging.AddOpenTelemetry(openTelemetryLoggerOptions =>
        {
            openTelemetryLoggerOptions.IncludeScopes = true;
            openTelemetryLoggerOptions.IncludeFormattedMessage = true;

            openTelemetryLoggerOptions.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri($"{seqConfig.Host}/ingest/otlp/v1/logs");
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Headers = $"X-Seq-ApiKey={seqConfig.ApiKey}";
            });
        });

    if (builder.Environment.IsDevelopment()) logging.AddConsole();
});

if (builder.Environment.IsDevelopment())
    builder.Services.AddCors(opt =>
    {
        opt.AddPolicy(defaultCorsPolicyName, policy =>
        {
            policy.AllowAnyHeader()
                .AllowAnyOrigin()
                .AllowAnyMethod();
        });
    });

if (builder.Environment.IsProduction())
    builder.Services.AddCors(opt =>
    {
        opt.AddPolicy(defaultCorsPolicyName, policy =>
        {
            var allowedOrigins = config.GetValue<string>("AllowedOrigins");

            if (string.IsNullOrWhiteSpace(allowedOrigins))
                throw new ApplicationException("Production Environment requires to provide allowed origins.");

            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
        options.SlidingExpiration = true;
        options.LoginPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    });

builder.Services.Configure<IdentityOptions>(options =>
{
    // Default Password settings.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 5;
    options.Password.RequiredUniqueChars = 1;
});

builder.Services.AddAuthModule()
    .AddIdentity<AppUser, IdentityRole>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddTokenProvider<DataProtectorTokenProvider<AppUser>>(TokenOptions.DefaultProvider);

builder.Services.AddAdministrationModule();
builder.Services.AddCryptoModule();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddSignalR();

builder.Services.AddResponseCompression(opts =>
{
    opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

builder.Services.AddHostedService<BackupWorker>();

var app = builder.Build();

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();

    app.MapOpenApi();
    app.MapScalarApiReference(opt =>
    {
        opt.WithTitle("OctoBackup Api");
        opt.WithTheme(ScalarTheme.BluePlanet);
        opt.WithDefaultHttpClient(ScalarTarget.Http, ScalarClient.Curl);
        opt.Servers = [];
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode();

app.UseRouting();

app.MapBackupModuleEndpoints();
app.MapBackupsTestsModuleEndpoints();
app.MapAuthModuleEndpoints();
app.MapAdministrationModuleEndpoints();

app.UseCors(defaultCorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapHub<BackupHub>(BackupHubHelper.HubUrl);

DbCommon.CreateDbDirectoryIfNotExists();

using var scope = app.Services.CreateScope();

var authDb = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
authDb.Database.Migrate();

var backupsDb = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
backupsDb.Database.Migrate();

app.Run();