using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Backup.Application.Services;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Api.Security;

namespace Modules.Backup.Api.Backups;

internal static class BackupEncryptionEndpoints
{
    public static void MapBackupEncryption(this WebApplication app)
    {
        var api = app.MapGroup("/api/backup-encryption").RequireAuthorization();
        api.MapGet("/availability", async (BackupEncryptionService service, HttpContext c) => Results.Ok(new { globalEnabled = await service.GlobalEnabled(c.RequestAborted) }));
        api.MapGet("/global", (HttpContext c, BackupEncryptionService service) => Safe(async () =>
            Results.Ok(await service.Status(null, c.User, c.RequestAborted))));
        api.MapPost("/global", (HttpContext c, BackupEncryptionService service, ChangeEncryptionKey request) => Safe(async () => {
            await service.Change(null, request, c.User, c.RequestAborted); return Results.NoContent(); })).RequireRateLimiting("auth");
        api.MapPost("/global/export", (HttpContext c, BackupEncryptionService service) => Safe(async () =>
            Results.Ok(new { password = await service.ExportGenerated(c.User, c.RequestAborted) }))).RequireRateLimiting("auth");
        api.MapGet("/servers/{id:guid}", (Guid id, HttpContext c, BackupEncryptionService service) => Safe(async () =>
            Results.Ok(await service.Status(id, c.User, c.RequestAborted))));
        api.MapPost("/servers/{id:guid}", (Guid id, HttpContext c, BackupEncryptionService service, ChangeEncryptionKey request) => Safe(async () => {
            await service.Change(id, request, c.User, c.RequestAborted); return Results.NoContent(); })).RequireRateLimiting("auth");

        // Bind id separately so the shared resource filter checks the backup ID.
        app.MapPost("/api/backups/DownloadDecrypted", (Guid id, DecryptBackupRequest request, HttpContext c,
            BackupsDbContext db, BackupEncryptionService service) => Safe(async () => {
                var backup = await db.Backups.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, c.RequestAborted);
                if (backup is null) return Results.NotFound();
                var stream = await service.OpenDecrypted(backup, request.Password, true, c.RequestAborted);
                var extension = backup.EncryptionKeyId is null ? Path.GetExtension(backup.FilePath) : Path.GetExtension(Path.GetFileNameWithoutExtension(backup.FilePath));
                return Results.Stream(stream, "application/octet-stream", $"backup-{backup.Id}{extension}");
            })).RequireAuthorization().RequireRateLimiting("auth").AddEndpointFilter<ResourceAccessFilter>();
    }

    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (BackupQueueException error) { return Results.Problem(statusCode: error.StatusCode, title: error.Message); }
    }
    internal sealed record DecryptBackupRequest(string? Password);
}
