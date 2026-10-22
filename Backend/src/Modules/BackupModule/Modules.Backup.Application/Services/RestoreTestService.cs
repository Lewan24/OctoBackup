using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Auth.Core.Entities;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Enums;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

public sealed class RestoreTestCoordinator
{
    public SemaphoreSlim Admission { get; } = new(1, 1);
    public SemaphoreSlim Execution { get; } = new(1, 1);
}

public sealed class RestoreTestService(BackupsDbContext db, IRestoreTestRunner runner, IConfiguration configuration,
    IApplicationSettings settings, RestoreTestCoordinator coordinator, UserManager<AppUser> users,
    BackupQueueService notifications, ILogger<RestoreTestService> logger)
{
    public static BackupTest Create(PerformedBackup backup, DbServerConnection server, string? userId, bool automatic)
    {
        var test = new BackupTest { Id = Guid.NewGuid(), BackupId = backup.Id, ActiveBackupId = backup.Id,
            Status = ETestStatus.Queued, TestedOn = DateTime.UtcNow, RequestedById = userId, Automatic = automatic,
            DatabaseType = backup.DatabaseType ?? server.DbType, DatabaseName = backup.DatabaseName ?? server.DbName };
        backup.TestId = test.Id;
        return test;
    }

    public async Task<RestoreReportDto> Enqueue(Guid id, ClaimsPrincipal user, CancellationToken token)
    {
        await coordinator.Admission.WaitAsync(token);
        try
        {
            var backup = await db.Backups.FirstOrDefaultAsync(b => b.Id == id, token)
                ?? throw new BackupQueueException(404, "Backup not found.");
            await Authorize(backup, user, token);
            if (!configuration.GetValue("RestoreTests:Enabled", false))
                throw new BackupQueueException(503, "Restore testing is not configured on this deployment.");
            ValidateArtifact(backup);
            if (await db.BackupsTests.AnyAsync(t => t.ActiveBackupId == id, token))
                throw new BackupQueueException(409, "A restore test is already queued or running for this backup.");
            if (await db.BackupsTests.CountAsync(t => t.ActiveBackupId != null, token) >= 100)
                throw new BackupQueueException(409, "The restore test queue is full. Try again later.");
            var server = await db.DbConnections.FirstOrDefaultAsync(s => s.Id == backup.ServerConnectionId, token)
                ?? throw new BackupQueueException(404, "The backup connection no longer exists.");
            var test = Create(backup, server, user.FindFirstValue(ClaimTypes.NameIdentifier), false);
            db.BackupsTests.Add(test);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteErrorCode: 19 })
            { throw new BackupQueueException(409, "A restore test is already active. Refresh the report."); }
            await notifications.Changed(server.Id);
            return Report(test);
        }
        finally { coordinator.Admission.Release(); }
    }

    public async Task<RestoreReportDto?> Get(Guid id, ClaimsPrincipal user, CancellationToken token)
    {
        var backup = await db.Backups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, token)
            ?? throw new BackupQueueException(404, "Backup not found.");
        await Authorize(backup, user, token);
        var test = await db.BackupsTests.AsNoTracking().FirstOrDefaultAsync(t => t.Id == backup.TestId, token);
        return test is null ? null : Report(test);
    }

    private async Task Authorize(PerformedBackup backup, ClaimsPrincipal user, CancellationToken token)
    {
        if (user.IsInRole(AppRoles.Admin)) return;
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ||
            !await db.UsersServers.AnyAsync(s => s.UserId == uid && s.ServerId == backup.ServerConnectionId, token) ||
            !await db.DbConnections.AnyAsync(s => s.Id == backup.ServerConnectionId && !s.IsDisabled, token))
            throw new BackupQueueException(403, "Access to this backup is not permitted.");
    }

    private static void ValidateArtifact(PerformedBackup backup)
    {
        if (backup.IsSoftDeleted || !SafeBackupPath.IsWithinRoot(backup.FilePath) || !File.Exists(backup.FilePath))
            throw new BackupQueueException(400, "The backup artifact is unavailable or expired.");
    }

    public async Task Recover(CancellationToken token)
    {
        await runner.CleanupOrphans(token);
        await db.BackupsTests.Where(t => t.Status == ETestStatus.Running).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.Status, ETestStatus.Interrupted).SetProperty(t => t.ActiveBackupId, (Guid?)null)
                .SetProperty(t => t.FinishedOn, DateTime.UtcNow)
                .SetProperty(t => t.ErrorMessage, "The API stopped during restore testing. Run another test."), token);
    }

    public Task MarkUnavailable(CancellationToken token) => db.BackupsTests.Where(t => t.Status == ETestStatus.Queued)
        .ExecuteUpdateAsync(u => u.SetProperty(t => t.ErrorMessage,
            "Waiting for the restore runtime. An administrator must check Docker access and deployment configuration."), token);

    public async Task SkipDisabledAutomaticTests(CancellationToken token)
    {
        if (settings.Current.AutomaticRestoreTests) return;
        await db.BackupsTests.Where(t => t.Status == ETestStatus.Queued && t.Automatic).ExecuteUpdateAsync(u =>
            u.SetProperty(t => t.Status, ETestStatus.Skipped).SetProperty(t => t.ActiveBackupId, (Guid?)null)
                .SetProperty(t => t.FinishedOn, DateTime.UtcNow)
                .SetProperty(t => t.ErrorMessage, "Automatic restore tests were disabled before this test started."), token);
    }

    public async Task<bool> ProcessNext(CancellationToken token)
    {
        if (!await coordinator.Execution.WaitAsync(0, token)) return false;
        try
        {
            var test = await db.BackupsTests.Where(t => t.Status == ETestStatus.Queued).OrderBy(t => t.TestedOn).FirstOrDefaultAsync(token);
            if (test is null) return false;
            var backup = await db.Backups.FirstOrDefaultAsync(b => b.Id == test.BackupId, token);
            try
            {
                if (test.Automatic && !settings.Current.AutomaticRestoreTests)
                {
                    test.Status = ETestStatus.Skipped;
                    test.ErrorMessage = "Automatic restore tests were disabled before this test started.";
                }
                else
                {
                    if (!configuration.GetValue("RestoreTests:Enabled", false))
                        throw new BackupQueueException(503, "Restore testing is not configured on this deployment.");
                    if (backup is null) throw new BackupQueueException(404, "Backup no longer exists.");
                    ValidateArtifact(backup);
                    if (test.RequestedById is not null)
                    {
                        var user = await users.FindByIdAsync(test.RequestedById);
                        if (user is null || user.IsBlocked || !user.EmailConfirmed)
                            throw new BackupQueueException(403, "The requesting account is no longer allowed to test backups.");
                        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                            new Claim(ClaimTypes.NameIdentifier, user.Id),
                            new Claim(ClaimTypes.Role, await users.IsInRoleAsync(user, AppRoles.Admin) ? AppRoles.Admin : "User") }, "restore-worker"));
                        await Authorize(backup, principal, token);
                    }
                    test.Status = ETestStatus.Running;
                    test.ErrorMessage = null;
                    await db.SaveChangesAsync(token);
                    if (backup.ServerConnectionId is Guid sid) await notifications.Changed(sid);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("RestoreTests:TimeoutSeconds", 1800), 30, 7200)));
                    var preview = await runner.Run(test, backup.FilePath, timeout.Token);
                    test.PreviewJson = JsonSerializer.Serialize(preview);
                    test.Status = ETestStatus.Success;
                }
            }
            catch (Exception error)
            {
                test.Status = token.IsCancellationRequested ? ETestStatus.Interrupted : ETestStatus.Failure;
                test.ErrorMessage = error is BackupQueueException safe ? safe.Message : error is RestoreTestException restore ? restore.Message :
                    error is OperationCanceledException ? "Restore test timed out or was interrupted." : "Restore test failed. Check runtime availability and image compatibility.";
                // Provider output may contain restored data; never log it.
                logger.LogWarning("Restore test {TestId} ended with {Status}: {Reason}", test.Id, test.Status, test.ErrorMessage);
            }
            finally
            {
                try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await runner.Cleanup(test.Id, cleanup.Token); }
                catch
                {
                    test.Status = ETestStatus.Failure;
                    test.ErrorMessage = (test.ErrorMessage is null ? "" : test.ErrorMessage + " ") +
                        "Test container cleanup failed. An administrator must inspect the runtime; cleanup retries on startup.";
                    logger.LogWarning("Restore test {TestId} cleanup failed; retrying orphan cleanup on startup", test.Id);
                }
                test.ActiveBackupId = null;
                test.FinishedOn = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                if (backup?.ServerConnectionId is Guid sid) await notifications.Changed(sid);
            }
            return true;
        }
        finally { coordinator.Execution.Release(); }
    }

    private static RestoreReportDto Report(BackupTest t) => new(t.Id, t.BackupId, t.Status, t.Automatic, t.TestedOn,
        t.FinishedOn, t.ErrorMessage, t.PreviewJson is null ? null : JsonSerializer.Deserialize<RestorePreview>(t.PreviewJson));
}

public sealed class RestoreTestException(string message) : Exception(message);
