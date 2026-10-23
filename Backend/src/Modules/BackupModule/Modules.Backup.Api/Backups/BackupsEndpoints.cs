using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Shared.Dtos;
using Modules.Shared.Attributes;
using OneOf.Types;
using Modules.Backup.Api.Security;
using Modules.Backup.Application.Services;

namespace Modules.Backup.Api.Backups;

internal static class BackupsEndpoints
{
    public static WebApplication MapBackupEndpoints(this WebApplication app)
    {
        app.MapBackupEncryption();
        var api = app.MapGroup("/api/backups")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<ResourceAccessFilter>();

        api.MapGet("GetAllBackups", BackupsOperations.GetAllBackups)
            .WithSummary("Get all user's backups");

        api.MapPost("GetBackups", BackupsOperations.GetBackups)
            .WithSummary("Get user's 25 backups with provided offset");

        api.MapPost("PerformBackup", BackupsOperations.PerformBackup)
            .WithSummary("Perform backup on provided server");

        api.MapPost("PerformSchedulesBackup", BackupsOperations.PerformSchedulesBackup)
            .WithSummary("Perform backup on pending schedules")
            .AddEndpointFilter<AdminTokenAuthorizationFilter>();

        api.MapPost("DeleteBackup", BackupsOperations.DeleteBackup)
            .WithSummary("Delete backup entry + backup file")
            .AddEndpointFilter<AdminTokenAuthorizationFilter>();

        api.MapGet("Download", BackupsOperations.DownloadBackup)
            .WithSummary("Download backup file from server");

        api.MapPost("TestBackup", BackupsOperations.TestBackup)
            .WithSummary("Test backup file in prepared test container");
        api.MapGet("RestoreReport", BackupsOperations.RestoreReport);
        api.MapGet("RestoreAvailability", async (IConfiguration config, Modules.Backup.Infrastructure.DbContexts.BackupsDbContext db, Guid? id) => {
            var type = id is null ? null : await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                db.Backups.Where(b => b.Id == id).Select(b => b.DatabaseType));
            return Results.Ok(new { enabled = type == Modules.Backup.Shared.Enums.DatabaseType.Sqlite
                ? config.GetValue("Sqlite:RestoreTestsEnabled", true) : config.GetValue("RestoreTests:Enabled", false) });
        });

        return app;
    }
}

internal abstract record BackupsOperations
{
    public static async Task<IResult> GetAllBackups(
        HttpContext context,
        [FromServices] IDbBackupService service)
    {
        var result = await service.GetAllBackups(context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> GetBackups(
        HttpContext context,
        [FromServices] IDbBackupService service,
        [FromBody] int offset = 0)
    {
        if (offset < 0) return Results.BadRequest("Offset must be nonnegative.");
        var result = await service.GetAllBackups(context.User.Identity?.Name);
        return result.Match<IResult>(items => Results.Ok(items.OrderByDescending(x => x.CreatedOn).Skip(offset).Take(25)), error => Results.BadRequest(error));
    }

    public static async Task<IResult> PerformBackup(
        HttpContext context,
        [FromServices] BackupQueueService queue,
        [FromBody] Guid serverId)
    {
        try { return Results.Accepted("/api/backup-queue", await queue.Enqueue(serverId, context.User, context.RequestAborted)); }
        catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
    }

    // TODO: idk why but AdminTokenFilter needs additional parameter to provide
    public static async Task<IResult> PerformSchedulesBackup(
        HttpContext context,
        [FromServices] BackupQueueService queue,
        [FromBody] bool test)
    {
        return Results.Accepted("/api/backup-queue", new { queued = await queue.EnqueueDueSchedules(context.RequestAborted) });
    }

    public static Task<IResult> DeleteBackup(
        HttpContext context,
        [FromBody] Guid id)
    {
        return Task.FromResult<IResult>(Results.Problem(statusCode: 501, title: "This operation is not implemented."));
    }

    public static async Task<IResult> DownloadBackup(
        HttpContext context,
        [FromServices] IDbBackupService service,
        [FromQuery] Guid id)
    {
        var result = await service.DownloadBackup(id, context.User.Identity?.Name);

        return result.Match<IResult>(
            file => TypedResults.PhysicalFile(file.FilePath, file.ContentType, file.FileName),
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> TestBackup(
        HttpContext context,
        [FromServices] RestoreTestService service,
        [FromBody] Guid id)
    {
        try { return Results.Accepted("/api/backups/RestoreReport?id=" + id, await service.Enqueue(id, context.User, context.RequestAborted)); }
        catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
    }

    public static async Task<IResult> RestoreReport(HttpContext context, [FromServices] RestoreTestService service, [FromQuery] Guid id)
    {
        context.Response.Headers.CacheControl = "no-store";
        // Ok(null) writes an empty body. Query clients need an explicit JSON null.
        try
        {
            var report = await service.Get(id, context.User, context.RequestAborted);
            return report is null ? Results.Text("null", "application/json") : Results.Ok(report);
        }
        catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
    }
}
