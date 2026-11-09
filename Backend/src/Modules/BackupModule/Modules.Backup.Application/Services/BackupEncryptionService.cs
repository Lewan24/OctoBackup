using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;

namespace Modules.Backup.Application.Services;

public sealed record EncryptionStatus(Guid? KeyId, bool UsesGlobalKey, bool CanExportGeneratedKey, bool GlobalEnabled, bool IsEncrypted, string Revision);
public sealed record ChangeEncryptionKey(Guid? ExpectedKeyId, string? OldPassword, string? NewPassword,
    string? ConfirmPassword, bool UseGlobal = false, bool Reset = false, bool? Enabled = null, string? ExpectedRevision = null);

public sealed class BackupEncryptionService(BackupsDbContext db, IDataProtectionProvider protection,
    ILogger<BackupEncryptionService> logger)
{
    private static readonly SemaphoreSlim Mutations = new(1, 1);
    private IDataProtector Protector => protection.CreateProtector("OctoBackup.BackupKeys.v1");
    private static string Scope(Guid? serverId) => serverId?.ToString("D") ?? "global";

    public async Task Initialize(CancellationToken token = default)
    {
        await Mutations.WaitAsync(token);
        try
        {
            // Fresh and upgraded installations start with global encryption off.
            // Existing keys remain available solely for historical artifacts until
            // an administrator explicitly enables a custom global password.
            if (!await db.BackupEncryptionPolicies.AnyAsync(token))
            {
                db.BackupEncryptionPolicies.Add(new BackupEncryptionPolicy());
                await db.SaveChangesAsync(token);
            }
            if (await db.Backups.AnyAsync(b => b.EncryptionKeyId != null &&
                !db.BackupEncryptionKeys.Any(k => k.Id == b.EncryptionKeyId), token))
                throw new InvalidOperationException("Backup encryption key metadata is missing. Restore the metadata and key ring.");
            // A hard kill may leave a named decrypted work file. Only this API
            // process owns storage; remove recognized work files before workers start.
            if (Directory.Exists(SafeBackupPath.Root))
                foreach (var path in Directory.EnumerateFiles(SafeBackupPath.Root, ".decrypt-*"))
                    if (SafeBackupPath.IsWithinRoot(path) && System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"\A\.decrypt-[a-f0-9]{32}\.(zip|bak|sqlite)\z"))
                        File.Delete(path);
            // Fail startup on a lost/wrong Data Protection ring, never silently regenerate.
            foreach (var key in await db.BackupEncryptionKeys.AsNoTracking().ToListAsync(token))
                CryptographicOperations.ZeroMemory(Protector.Unprotect(Convert.FromBase64String(key.ProtectedKey)));
        }
        finally { Mutations.Release(); }
    }

    public async Task Authorize(Guid? serverId, ClaimsPrincipal user, CancellationToken token)
    {
        if (serverId is null)
        {
            if (!user.IsInRole(AppRoles.Admin)) throw new BackupQueueException(403, "Administrator access is required.");
            return;
        }
        if (!await db.DbConnections.AnyAsync(s => s.Id == serverId, token)) throw new BackupQueueException(404, "Connection not found.");
        if (user.IsInRole(AppRoles.Admin)) return;
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ||
            !await db.UsersServers.AnyAsync(s => s.UserId == uid && s.ServerId == serverId, token) ||
            !await db.DbConnections.AnyAsync(s => s.Id == serverId && !s.IsDisabled, token))
            throw new BackupQueueException(403, "Access to this connection is not permitted.");
    }

    public async Task<bool> GlobalEnabled(CancellationToken token = default) =>
        await db.BackupEncryptionPolicies.AsNoTracking().Where(p => p.Id == 1).Select(p => p.GlobalEnabled).SingleAsync(token);

    public async Task<BackupEncryptionKey?> Effective(Guid? serverId, CancellationToken token)
    {
        if (serverId is not null && await db.BackupEncryptionKeys.AsNoTracking().SingleOrDefaultAsync(k => k.ActiveScope == Scope(serverId), token) is { } individual)
            return individual;
        if (!await GlobalEnabled(token)) return null;
        return await db.BackupEncryptionKeys.AsNoTracking().SingleOrDefaultAsync(k => k.ActiveScope == "global", token)
            ?? throw new InvalidOperationException("Enabled global encryption has no key. Restore encryption metadata.");
    }

    public async Task<EncryptionStatus> Status(Guid? serverId, ClaimsPrincipal user, CancellationToken token)
    {
        await Authorize(serverId, user, token);
        var key = await Effective(serverId, token);
        var policy = await db.BackupEncryptionPolicies.AsNoTracking().SingleAsync(p => p.Id == 1, token);
        return new(key?.Id, key is null || key.Scope == "global",
            serverId is null && await db.BackupEncryptionKeys.AnyAsync(k => k.Scope == "global" && k.ProtectedRecoveryPassword != null, token),
            policy.GlobalEnabled, key is not null, policy.Revision);
    }

    public async Task Change(Guid? serverId, ChangeEncryptionKey request, ClaimsPrincipal user, CancellationToken token)
    {
        await Authorize(serverId, user, token);
        if (request.Reset && !user.IsInRole(AppRoles.Admin)) throw new BackupQueueException(403, "Only administrators may reset a key.");
        if (request.UseGlobal && serverId is null) throw new BackupQueueException(400, "The global key cannot inherit another key.");
        if (serverId is null && request.Enabled is null) throw new BackupQueueException(400, "Specify whether global encryption should be enabled.");
        var disableGlobal = serverId is null && request.Enabled == false;
        if (!request.UseGlobal && !disableGlobal) ValidatePassword(request.NewPassword, request.ConfirmPassword);
        await Mutations.WaitAsync(token);
        try
        {
            var current = await Effective(serverId, token);
            var policy = await db.BackupEncryptionPolicies.AsNoTracking().SingleAsync(p => p.Id == 1, token);
            if (request.ExpectedKeyId != current?.Id || (request.ExpectedRevision is not null && request.ExpectedRevision != policy.Revision))
                throw new BackupQueueException(409, "Encryption settings changed. Reload before saving.");
            var scope = Scope(serverId);
            if (!disableGlobal && !request.Reset && current?.Scope == scope) Verify(current, request.OldPassword);
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (!disableGlobal)
            {
                await db.BackupEncryptionKeys.Where(k => k.ActiveScope == scope)
                    .ExecuteUpdateAsync(u => u.SetProperty(k => k.ActiveScope, (string?)null), token);
                if (!request.UseGlobal) db.BackupEncryptionKeys.Add(Create(scope, request.NewPassword!));
            }
            if (serverId is null)
            {
                var nextRevision = Guid.NewGuid().ToString("N");
                var changed = await db.BackupEncryptionPolicies.Where(p => p.Id == 1 && p.Revision == policy.Revision)
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.GlobalEnabled, !disableGlobal).SetProperty(p => p.Revision, nextRevision), token);
                if (changed != 1) throw new BackupQueueException(409, "Encryption settings changed. Reload before saving.");
            }
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            logger.LogInformation("User {Actor} changed backup encryption scope {Scope}; global enabled: {Enabled}; administrator reset: {Reset}",
                user.FindFirstValue(ClaimTypes.NameIdentifier), scope, serverId is null ? !disableGlobal : policy.GlobalEnabled, request.Reset);
        }
        finally { Mutations.Release(); }
    }

    public async Task<string> ExportGenerated(ClaimsPrincipal user, CancellationToken token)
    {
        await Authorize(null, user, token);
        var key = await db.BackupEncryptionKeys.AsNoTracking().Where(k => k.Scope == "global" && k.ProtectedRecoveryPassword != null)
            .OrderByDescending(k => k.CreatedAt).FirstOrDefaultAsync(token);
        if (key is null) throw new BackupQueueException(400, "No previous generated key is available. Custom passwords cannot be revealed.");
        logger.LogInformation("Administrator {Actor} exported a legacy generated backup key", user.FindFirstValue(ClaimTypes.NameIdentifier));
        return Protector.Unprotect(key.ProtectedRecoveryPassword!);
    }

    private static void ValidatePassword(string? password, string? confirmation)
    {
        if (password is null || password.Length is < 16 or > 512 || password != confirmation)
            throw new BackupQueueException(400, "Enter matching new encryption passwords of 16–512 characters.");
    }

    // Server configuration and its encryption key commit in one transaction.
    public async Task<IAsyncDisposable> LockConfiguration()
    {
        await Mutations.WaitAsync();
        return new ConfigurationLease();
    }
    private sealed class ConfigurationLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { Mutations.Release(); return ValueTask.CompletedTask; }
    }

    public async Task ApplyServerConfiguration(Guid serverId, Modules.Backup.Shared.Dtos.ServerEncryptionOptions? options)
    {
        if (options is null) return; // Existing API clients preserve individual keys on edit.
        if (options.Mode is not ("inherit" or "individual")) throw new BackupQueueException(400, "Choose global settings or an individual encryption password.");
        var scope = Scope(serverId);
        var old = await db.BackupEncryptionKeys.AsNoTracking().SingleOrDefaultAsync(k => k.ActiveScope == scope);
        if (old?.Id != options.ExpectedKeyId) throw new BackupQueueException(409, "The individual encryption key changed. Reload this connection.");
        if (options.Mode == "individual") ValidatePassword(options.Password, options.ConfirmPassword);
        if (old is not null) Verify(old, options.CurrentPassword);
        await db.BackupEncryptionKeys.Where(k => k.ActiveScope == scope).ExecuteUpdateAsync(u => u.SetProperty(k => k.ActiveScope, (string?)null));
        if (options.Mode == "individual") db.BackupEncryptionKeys.Add(Create(scope, options.Password!));
    }

    private BackupEncryptionKey Create(string scope, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var raw = BackupEncryptionFormat.Derive(password, salt);
        try { return new() { Scope = scope, ActiveScope = scope, Salt = Convert.ToBase64String(salt), ProtectedKey = Convert.ToBase64String(Protector.Protect(raw)) }; }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private void Verify(BackupEncryptionKey key, string? password)
    {
        if (password is null || password.Length is < 1 or > 512) throw new BackupQueueException(400, "The encryption password is incorrect.");
        var supplied = BackupEncryptionFormat.Derive(password, Convert.FromBase64String(key.Salt));
        var actual = Protector.Unprotect(Convert.FromBase64String(key.ProtectedKey));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(supplied, actual)) throw new BackupQueueException(400, "The encryption password is incorrect.");
        }
        finally { CryptographicOperations.ZeroMemory(supplied); CryptographicOperations.ZeroMemory(actual); }
    }

    public async Task<(string Path, Guid? KeyId)> Encrypt(string path, Guid serverId, CancellationToken token)
    {
        if (!SafeBackupPath.IsWithinRoot(path)) throw new BackupQueueException(400, "Invalid backup artifact path.");
        var key = await Effective(serverId, token);
        if (key is null) return (path, null);
        var raw = Protector.Unprotect(Convert.FromBase64String(key.ProtectedKey));
        var output = path + ".enc";
        try
        {
            await using (var source = File.OpenRead(path))
            await using (var destination = PrivateFile(output, FileOptions.Asynchronous))
            {
                await BackupEncryptionFormat.Encrypt(source, destination, key.Id, Convert.FromBase64String(key.Salt), raw, token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
            File.Delete(path);
            return (output, key.Id);
        }
        catch { if (File.Exists(output)) File.Delete(output); throw; }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    // Authenticate the complete artifact before releasing a download or running a
    // restore. DeleteOnClose handles exceptions, disconnects and normal disposal.
    public async Task<FileStream> OpenDecrypted(PerformedBackup backup, string? password, bool verifyPassword, CancellationToken token)
    {
        if (backup.IsSoftDeleted || !SafeBackupPath.IsWithinRoot(backup.FilePath) || !File.Exists(backup.FilePath))
            throw new BackupQueueException(400, "Backup artifact is unavailable or expired.");
        if (backup.EncryptionKeyId is null) return File.OpenRead(backup.FilePath);
        var key = await db.BackupEncryptionKeys.AsNoTracking().SingleAsync(k => k.Id == backup.EncryptionKeyId, token);
        if (verifyPassword) Verify(key, password);
        var raw = Protector.Unprotect(Convert.FromBase64String(key.ProtectedKey));
        var temp = Path.Combine(SafeBackupPath.Root, $".decrypt-{Guid.NewGuid():N}" + Path.GetExtension(Path.GetFileNameWithoutExtension(backup.FilePath)));
        var output = PrivateFile(temp, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await using var source = File.OpenRead(backup.FilePath);
            await BackupEncryptionFormat.Decrypt(source, output, raw, key.Id, token);
            await output.FlushAsync(token);
            output.Position = 0;
            return output;
        }
        catch (OperationCanceledException) { await output.DisposeAsync(); throw; }
        catch { await output.DisposeAsync(); throw new BackupQueueException(400, "The encrypted backup is damaged or cannot be decrypted."); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private static FileStream PrivateFile(string path, FileOptions options)
    {
        var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
            Share = FileShare.Read | FileShare.Delete, Options = options };
        if (!OperatingSystem.IsWindows()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new(path, settings);
    }
}
