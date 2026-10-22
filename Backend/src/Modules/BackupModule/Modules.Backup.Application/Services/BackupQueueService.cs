using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Helpers;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

public sealed class BackupQueueException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class BackupQueueService(BackupsDbContext db, IApplicationSettings settings, TimeProvider clock,
    BackupQueueCoordinator coordinator, NotifyService notify, ILogger<BackupQueueService> logger)
{
    public const int MaxActiveRequests = 1000;

    public async Task<BackupRequestDto> Enqueue(Guid serverId, ClaimsPrincipal user, CancellationToken token = default)
    {
        await coordinator.Mutations.WaitAsync(token);
        try
        {
            if (!await HasAccess(serverId, user, token)) throw new BackupQueueException(403, "You do not have access to this server.");
            var (server, tunnel) = await ValidateTarget(serverId, token);
            var key = BackupTarget.Key(server, tunnel);
            if (await db.BackupRequests.AnyAsync(r => r.ActiveDatabaseKey == key, token))
                throw new BackupQueueException(409, "A backup for this database is already queued or running. View the backup queue before requesting another.");
            if (await db.BackupRequests.CountAsync(r => r.ActiveDatabaseKey != null, token) >= MaxActiveRequests)
                throw new BackupQueueException(409, "The backup queue is full. Wait for requests to finish.");
            var request = NewRequest(server, key, user.FindFirstValue(ClaimTypes.NameIdentifier), user.Identity?.Name);
            db.BackupRequests.Add(request);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteErrorCode: 19 })
            {
                db.ChangeTracker.Clear();
                throw new BackupQueueException(409, "A backup for this database is already queued or running.");
            }
            logger.LogInformation("User {UserId} queued backup request {RequestId} for server {ServerId}", request.RequestedById, request.Id, serverId);
            await Changed(serverId);
            return Dto(request, user, [serverId]);
        }
        finally { coordinator.Mutations.Release(); }
    }

    public async Task<BackupQueueDto> Get(ClaimsPrincipal user, int offset = 0, int limit = 25, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new BackupQueueException(400, "Invalid queue page.");
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var assigned = await db.UsersServers.Where(x => x.UserId == Guid.Parse(uid)).Select(x => x.ServerId).ToListAsync(token);
        var visible = db.BackupRequests.AsNoTracking().Where(r => !r.IsHidden);
        if (!user.IsInRole(AppRoles.Admin)) visible = visible.Where(r => r.RequestedById == uid || assigned.Contains(r.ServerId));
        var rows = await visible.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip(offset).Take(limit).ToListAsync(token);
        var running = await visible.Where(r => r.Status == BackupRequestStatus.Running || r.Status == BackupRequestStatus.Cancelling).OrderBy(r => r.StartedAt).FirstOrDefaultAsync(token);
        var next = await visible.Where(r => r.Status == BackupRequestStatus.Queued).OrderBy(r => r.EligibleAt).ThenBy(r => r.CreatedAt).FirstOrDefaultAsync(token);
        var policy = settings.Current;
        return new(rows.Select(r => Dto(r, user, assigned)).ToList(), await visible.CountAsync(token),
            await visible.Where(r => r.ActiveDatabaseKey != null).Select(r => r.ServerId).Distinct().ToListAsync(token),
            await visible.CountAsync(r => r.Status == BackupRequestStatus.Queued, token),
            await visible.CountAsync(r => r.Status == BackupRequestStatus.Running || r.Status == BackupRequestStatus.Cancelling, token),
            await db.BackupRequests.AnyAsync(r => r.Status == BackupRequestStatus.Running || r.Status == BackupRequestStatus.Cancelling, token),
            running is null ? null : Dto(running, user, assigned), next is null ? null : Dto(next, user, assigned),
            policy.BackupQueueDelaySeconds, policy.BackupQueueSpacingSeconds, clock.GetUtcNow());
    }

    public async Task CancelOrDelete(Guid id, ClaimsPrincipal user, bool delete, CancellationToken token = default)
    {
        await coordinator.Mutations.WaitAsync(token);
        try
        {
            var request = await db.BackupRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && !r.IsHidden, token);
            if (request is null) throw new BackupQueueException(404, "Queue request not found.");
            var uid = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var canManage = user.IsInRole(AppRoles.Admin) || request.RequestedById == uid ||
                (request.RequestedById is null && await HasAccess(request.ServerId, user, token));
            if (!canManage) throw new BackupQueueException(403, "You cannot change this backup request.");
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var rows = await db.BackupRequests.Where(r => r.Id == id && r.Status == BackupRequestStatus.Queued)
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, BackupRequestStatus.Cancelled)
                    .SetProperty(r => r.FinishedAt, now).SetProperty(r => r.ActiveDatabaseKey, (string?)null)
                    .SetProperty(r => r.IsHidden, delete).SetProperty(r => r.Message, "Cancelled before execution."), token);
            if (rows == 0)
            {
                if (delete)
                {
                    rows = await db.BackupRequests.Where(r => r.Id == id && r.ActiveDatabaseKey == null)
                        .ExecuteUpdateAsync(update => update.SetProperty(r => r.IsHidden, true), token);
                    if (rows == 0) throw new BackupQueueException(409, "Cancel this running request and wait for it to stop before removing it.");
                }
                else
                {
                    rows = await db.BackupRequests.Where(r => r.Id == id && r.Status == BackupRequestStatus.Running)
                        .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, BackupRequestStatus.Cancelling)
                            .SetProperty(r => r.Message, "Cancellation requested. Waiting for export cleanup."), token);
                    if (rows == 0) throw new BackupQueueException(409, "The request has already finished or is being cancelled. Refresh the queue.");
                }
            }
            logger.LogInformation("User {UserId} requested {Action} for backup request {RequestId}", uid, delete ? "removal" : "cancellation", id);
            await Changed(request.ServerId);
        }
        finally { coordinator.Mutations.Release(); }
    }

    public async Task<int> EnqueueDueSchedules(CancellationToken token = default)
    {
        await coordinator.Mutations.WaitAsync(token);
        var changed = new HashSet<Guid>();
        var accepted = 0;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var now = clock.GetLocalNow().DateTime;
            var due = await db.Schedules.Where(s => s.IsEnabled && s.NextBackupDate <= now).OrderBy(s => s.NextBackupDate).Take(100).ToListAsync(token);
            foreach (var schedule in due)
            {
                BackupScheduleConfiguration? config;
                try { config = schedule.Configuration; }
                catch (JsonException)
                {
                    logger.LogWarning("Schedule {ScheduleId} has invalid configuration; repair it before retrying", schedule.Id);
                    continue;
                }
                if (config?.Days is not { Count: > 0 } || config.Times is not { Count: > 0 } || config.Days.Any(day => !Enum.IsDefined(day)))
                {
                    logger.LogWarning("Schedule {ScheduleId} has no valid recurrence; repair it before retrying", schedule.Id);
                    continue;
                }
                DbServerConnection server; DbServerTunnel? tunnel;
                try { (server, tunnel) = await ValidateTarget(schedule.DbConnectionId, token); }
                catch (BackupQueueException error) { logger.LogWarning("Schedule {ScheduleId} not queued: {Reason}", schedule.Id, error.Message); continue; }
                var key = BackupTarget.Key(server, tunnel);
                var request = await db.BackupRequests.FirstOrDefaultAsync(r => r.ActiveDatabaseKey == key, token);
                // An alias connection keeps its own schedule/history ownership. Wait
                // for the other connection's active request rather than consuming it.
                if (request is not null && (request.ServerId != server.Id || request.Status == BackupRequestStatus.Cancelling)) continue;
                if (request is null)
                {
                    if (await db.BackupRequests.CountAsync(r => r.ActiveDatabaseKey != null, token) >= MaxActiveRequests) break;
                    request = NewRequest(server, key, null, null);
                    request.Source = "Scheduled";
                    db.BackupRequests.Add(request);
                    accepted++;
                }
                var scheduleIds = JsonSerializer.Deserialize<List<Guid>>(request.ScheduleIdsJson) ?? [];
                if (!scheduleIds.Contains(schedule.Id)) scheduleIds.Add(schedule.Id);
                request.ScheduleIdsJson = JsonSerializer.Serialize(scheduleIds);
                if (request.RequestedById is not null) request.Source = "Manual + scheduled";
                schedule.NextBackupDate = BackupScheduleHelper.GetNextDateTime(config.Days, config.Times, now);
                // Acceptance and occurrence advancement commit together. Cancelling this
                // request skips this occurrence instead of immediately re-enqueuing it.
                await db.SaveChangesAsync(token);
                changed.Add(server.Id);
            }
            await transaction.CommitAsync(token);
        }
        finally { coordinator.Mutations.Release(); }
        foreach (var server in changed) await Changed(server);
        return accepted;
    }

    public async Task<(DbServerConnection Server, DbServerTunnel? Tunnel)> ValidateTarget(Guid id, CancellationToken token = default)
    {
        var server = await db.DbConnections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, token);
        if (server is null || server.IsDisabled) throw new BackupQueueException(400, "The server is disabled or no longer available.");
        var tunnel = server.IsTunnelRequired ? await db.DbServerTunnels.AsNoTracking().FirstOrDefaultAsync(t => t.Id == server.TunnelId, token) : null;
        if (!DatabaseHostPolicy.AllowsConnection(settings.Current, server.ServerHost, server.IsTunnelRequired, tunnel?.ServerHost, tunnel?.RemoteHost))
            throw new BackupQueueException(400, DatabaseHostPolicy.DeniedMessage);
        return (server, tunnel);
    }

    public Task<bool> HasAccess(Guid id, ClaimsPrincipal user, CancellationToken token = default)
    {
        if (user.IsInRole(AppRoles.Admin)) return Task.FromResult(true);
        var uid = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return db.UsersServers.AnyAsync(s => s.ServerId == id && s.UserId == uid, token);
    }

    public async Task Changed(Guid serverId)
    {
        try { await notify.CallQueueChangedEvent(serverId); }
        catch (Exception error) { logger.LogWarning(error, "Queue notification failed for {ServerId}; clients will poll", serverId); }
    }

    private BackupRequest NewRequest(DbServerConnection server, string key, string? userId, string? name)
    {
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        return new() { ServerId = server.Id, ServerName = server.ConnectionName, DatabaseName = server.DbName,
            TargetKey = key, ActiveDatabaseKey = key, RequestedById = userId, RequestedByName = name,
            CreatedAt = now, EligibleAt = now + settings.Current.BackupQueueDelaySeconds * 1000L };
    }

    private static BackupRequestDto Dto(BackupRequest request, ClaimsPrincipal user, ICollection<Guid> assigned)
    {
        var own = request.RequestedById == user.FindFirstValue(ClaimTypes.NameIdentifier);
        var manage = user.IsInRole(AppRoles.Admin) || own || (request.RequestedById is null && assigned.Contains(request.ServerId));
        return new(request.Id, request.ServerId, request.ServerName, request.DatabaseName, request.Source,
            request.RequestedById is null ? "Scheduler" : own ? "You" : user.IsInRole(AppRoles.Admin) ? request.RequestedByName ?? "Operator" : "Another operator",
            request.Status, DateTimeOffset.FromUnixTimeMilliseconds(request.CreatedAt), DateTimeOffset.FromUnixTimeMilliseconds(request.EligibleAt),
            request.StartedAt is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(request.StartedAt.Value),
            request.FinishedAt is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(request.FinishedAt.Value), request.Message, request.BackupId,
            manage && request.Status is BackupRequestStatus.Queued or BackupRequestStatus.Running,
            manage && request.Status is not (BackupRequestStatus.Running or BackupRequestStatus.Cancelling));
    }
}
