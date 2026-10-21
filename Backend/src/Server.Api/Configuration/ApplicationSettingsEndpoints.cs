using System.Security.Claims;
using Modules.Shared.Settings;
using Modules.Auth.Shared.Static.Entities;

namespace Server.Api.Configuration;

public static class ApplicationSettingsEndpoints
{
    public static void MapApplicationSettings(this WebApplication app)
    {
        var group = app.MapGroup("/api/administration/settings")
            .RequireAuthorization(policy => policy.RequireRole(AppRoles.Admin));
        group.MapGet("", (IApplicationSettings settings) => Results.Ok(settings.Current));
        group.MapPost("", async (ApplicationSettings proposed, FileApplicationSettings settings, HttpContext context, ILogger<FileApplicationSettings> logger, CancellationToken token) =>
        {
            try
            {
                var actor = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                var saved = await settings.Save(proposed, actor, token);
                if (saved is null) return Results.Problem(statusCode: 409, title: "Settings changed in another session. Reload settings and apply your changes again.");
                logger.LogInformation("Administrator {UserId} saved application settings revision {Revision}. Hosts: {HostCount}, registration: {Registration}, auto-confirm: {AutoConfirm}, retention days: {Retention}, polling seconds: {Polling}, queue delay seconds: {QueueDelay}, queue spacing seconds: {QueueSpacing}",
                    actor, saved.Revision, saved.AllowedDatabaseHosts.Length, saved.RegistrationEnabled, saved.AutoConfirmAccounts, saved.DefaultRetentionDays, saved.SchedulePollSeconds, saved.BackupQueueDelaySeconds, saved.BackupQueueSpacingSeconds);
                return Results.Ok(saved);
            }
            catch (ArgumentException e) { return Results.Problem(statusCode: 400, title: e.Message); }
        });
        // Only non-sensitive registration behavior is public; hosts and audit data
        // remain administrator-only.
        app.MapGet("/api/auth/registration-policy", (IApplicationSettings settings) =>
        {
            var current = settings.Current;
            return Results.Ok(new { enabled = current.RegistrationEnabled, autoConfirm = current.AutoConfirmAccounts });
        }).AllowAnonymous();
    }
}
