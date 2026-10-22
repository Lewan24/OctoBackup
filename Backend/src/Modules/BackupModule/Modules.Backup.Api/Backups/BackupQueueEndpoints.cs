using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.Backup.Application.Services;

namespace Modules.Backup.Api.Backups;

public static class BackupQueueEndpoints
{
    public static void MapBackupQueue(this WebApplication app)
    {
        var api = app.MapGroup("/api/backup-queue").RequireAuthorization();
        api.MapGet("", async (HttpContext context, BackupQueueService queue, int offset = 0, int limit = 25) =>
        {
            try { return Results.Ok(await queue.Get(context.User, offset, limit, context.RequestAborted)); }
            catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
        });
        api.MapPost("Cancel", async (HttpContext context, BackupQueueService queue, [FromBody] Guid id) =>
        {
            try { await queue.CancelOrDelete(id, context.User, false, context.RequestAborted); return Results.NoContent(); }
            catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
        });
        api.MapPost("Delete", async (HttpContext context, BackupQueueService queue, [FromBody] Guid id) =>
        {
            try { await queue.CancelOrDelete(id, context.User, true, context.RequestAborted); return Results.NoContent(); }
            catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
        });
    }
}
