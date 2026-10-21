using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Auth.Core.Entities;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

public sealed class BackupQueueProcessor(BackupsDbContext db, BackupQueueService queue, IBackupExecutor executor,
    UserManager<AppUser> users, BackupQueueCoordinator coordinator, IServiceScopeFactory scopes,
    IApplicationSettings settings, TimeProvider clock, ILogger<BackupQueueProcessor> logger)
{
    public async Task RecoverInterrupted(CancellationToken token = default)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var servers = await db.BackupRequests.Where(r => r.Status == BackupRequestStatus.Running || r.Status == BackupRequestStatus.Cancelling)
            .Select(r => r.ServerId).Distinct().ToListAsync(token);
        await db.BackupRequests.Where(r => r.Status == BackupRequestStatus.Running || r.Status == BackupRequestStatus.Cancelling)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, BackupRequestStatus.Interrupted)
                .SetProperty(r => r.ActiveDatabaseKey, (string?)null).SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.Message, "The API stopped during this backup. Check backup history before submitting another request."), token);
        foreach (var server in servers) await queue.Changed(server);
    }

    public async Task<bool> ProcessNext(CancellationToken stoppingToken = default)
    {
        if (!await coordinator.Execution.WaitAsync(0, stoppingToken)) return false;
        try
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var lastFinish = await db.BackupRequests.Where(r => r.StartedAt != null && r.FinishedAt != null)
                .OrderByDescending(r => r.FinishedAt).Select(r => r.FinishedAt).FirstOrDefaultAsync(stoppingToken);
            if (lastFinish is not null && now < lastFinish + settings.Current.BackupQueueSpacingSeconds * 1000L) return false;
            var request = await db.BackupRequests.AsNoTracking().Where(r => r.Status == BackupRequestStatus.Queued && r.EligibleAt <= now)
                .OrderBy(r => r.EligibleAt).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).FirstOrDefaultAsync(stoppingToken);
            if (request is null) return false;
            var claimed = await db.BackupRequests.Where(r => r.Id == request.Id && r.Status == BackupRequestStatus.Queued)
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, BackupRequestStatus.Running).SetProperty(r => r.StartedAt, now), stoppingToken);
            if (claimed == 0) return false;
            await queue.Changed(request.ServerId);

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var monitor = MonitorCancellation(request.Id, cancellation, monitorStop.Token);
            BackupArtifact? artifact = null;
            var committed = false;
            var commitAttempted = false;
            var preserveArtifact = false;
            try
            {
                await ValidateExecution(request, cancellation.Token);
                artifact = await executor.Execute(request, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!SafeBackupPath.IsWithinRoot(artifact.FilePath) || !File.Exists(artifact.FilePath))
                    throw new InvalidOperationException("Backup artifact is missing or outside storage.");
                // This conditional state transition arbitrates cancellation versus
                // completion. Backup metadata and terminal queue state commit together.
                await using (var transaction = await db.Database.BeginTransactionAsync(cancellation.Token))
                {
                    var finished = clock.GetUtcNow().ToUnixTimeMilliseconds();
                    var completed = await db.BackupRequests.Where(r => r.Id == request.Id && r.Status == BackupRequestStatus.Running)
                        .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, BackupRequestStatus.Succeeded)
                            .SetProperty(r => r.ActiveDatabaseKey, (string?)null).SetProperty(r => r.FinishedAt, finished)
                            .SetProperty(r => r.BackupId, artifact.Id).SetProperty(r => r.Message, "Backup completed."), cancellation.Token);
                    if (completed == 0) throw new OperationCanceledException("Cancellation won completion.");
                    db.Backups.Add(new PerformedBackup { Id = artifact.Id, ServerConnectionId = request.ServerId,
                        Name = artifact.Name, CreatedOn = clock.GetLocalNow().DateTime, FilePath = artifact.FilePath });
                    await db.SaveChangesAsync(cancellation.Token);
                    commitAttempted = true;
                    await transaction.CommitAsync(cancellation.Token);
                    committed = true;
                }
                await Retention(request.ServerId);
            }
            catch (Exception error)
            {
                // A commit acknowledgement can be interrupted. Never remove an artifact
                // when the durable transaction already recorded a successful backup.
                BackupRequest stored;
                try { stored = await db.BackupRequests.AsNoTracking().FirstAsync(r => r.Id == request.Id, CancellationToken.None); }
                catch { preserveArtifact = commitAttempted; throw; }
                committed = stored.Status == BackupRequestStatus.Succeeded;
                if (!committed)
                {
                    var cancelled = stored.Status == BackupRequestStatus.Cancelling;
                    var status = cancelled ? BackupRequestStatus.Cancelled : stoppingToken.IsCancellationRequested ? BackupRequestStatus.Interrupted : BackupRequestStatus.Failed;
                    var message = cancelled ? "Cancelled. No backup was published." : stoppingToken.IsCancellationRequested ?
                        "The API stopped during this backup. Check history before retrying." : error is DumpFailureException dump ? dump.SafeMessage :
                        error is BackupQueueException validation ? validation.Message : "Backup failed. Check server logs with this request ID.";
                    await db.BackupRequests.Where(r => r.Id == request.Id && r.ActiveDatabaseKey != null)
                        .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, status).SetProperty(r => r.ActiveDatabaseKey, (string?)null)
                            .SetProperty(r => r.FinishedAt, clock.GetUtcNow().ToUnixTimeMilliseconds()).SetProperty(r => r.Message, message), CancellationToken.None);
                    if (status == BackupRequestStatus.Failed) logger.LogWarning(error, "Backup request {RequestId} failed", request.Id);
                }
            }
            finally
            {
                monitorStop.Cancel();
                try { await monitor; } catch (OperationCanceledException) { }
                if (!committed && !preserveArtifact && artifact is not null && SafeBackupPath.IsWithinRoot(artifact.FilePath))
                {
                    try { if (File.Exists(artifact.FilePath)) File.Delete(artifact.FilePath); }
                    catch (Exception error) { logger.LogError(error, "Partial artifact cleanup failed for request {RequestId}", request.Id); }
                }
                await queue.Changed(request.ServerId);
            }
            return true;
        }
        finally { coordinator.Execution.Release(); }
    }

    private async Task ValidateExecution(BackupRequest request, CancellationToken token)
    {
        var (server, tunnel) = await queue.ValidateTarget(request.ServerId, token);
        if (BackupTarget.Key(server, tunnel) != request.TargetKey)
            throw new BackupQueueException(409, "The database target changed after enqueueing. Submit a new request.");
        if (request.RequestedById is not null)
        {
            var user = await users.FindByIdAsync(request.RequestedById);
            if (user is null || user.IsBlocked || !user.EmailConfirmed) throw new BackupQueueException(403, "The requesting account is no longer permitted to run backups.");
            if (!await users.IsInRoleAsync(user, AppRoles.Admin) &&
                !await db.UsersServers.AnyAsync(s => s.UserId == Guid.Parse(user.Id) && s.ServerId == request.ServerId, token))
                throw new BackupQueueException(403, "Server access was revoked after this backup was queued.");
        }
        else
        {
            var ids = JsonSerializer.Deserialize<List<Guid>>(request.ScheduleIdsJson) ?? [];
            if (!await db.Schedules.AnyAsync(s => ids.Contains(s.Id) && s.IsEnabled && s.DbConnectionId == request.ServerId, token))
                throw new BackupQueueException(400, "The schedules for this request were disabled, deleted or moved.");
        }
    }

    private async Task MonitorCancellation(Guid id, CancellationTokenSource cancellation, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(250, token);
            try
            {
                using var scope = scopes.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
                if (await context.BackupRequests.AnyAsync(r => r.Id == id && r.Status == BackupRequestStatus.Cancelling, token))
                {
                    cancellation.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning(error, "Could not inspect cancellation for {RequestId}", id); }
        }
    }

    private async Task Retention(Guid serverId)
    {
        try
        {
            var config = await db.Configurations.AsNoTracking().FirstOrDefaultAsync(c => c.ServerId == serverId);
            if (config is null) return;
            var cutoff = clock.GetLocalNow().DateTime.AddDays(-config.TimeInDaysToHoldBackups);
            var expired = await db.Backups.Where(b => b.ServerConnectionId == serverId && b.CreatedOn < cutoff && !b.IsSoftDeleted).ToListAsync();
            foreach (var backup in expired)
            {
                if (!SafeBackupPath.IsWithinRoot(backup.FilePath)) continue;
                if (File.Exists(backup.FilePath)) File.Delete(backup.FilePath);
                backup.IsSoftDeleted = true;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception error) { logger.LogWarning(error, "Retention cleanup failed after successful backup for {ServerId}", serverId); }
    }
}
