using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.Databases;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.Interfaces;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Enums;
using Modules.Crypto.Shared.Interfaces;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

public sealed class BackupExecutor(BackupsDbContext db, BackupQueueService queue, ICryptoService crypto,
    IApplicationSettings settings, BackupEncryptionService encryption, IConfiguration configuration, ILogger<BackupExecutor> logger) : IBackupExecutor
{
    public async Task<BackupArtifact> Execute(BackupRequest request, CancellationToken token)
    {
        var (server, tunnel) = await queue.ValidateTarget(request.ServerId, token);
        if (BackupTarget.Key(server, tunnel) != request.TargetKey)
            throw new BackupQueueException(409, "The database target changed after this request was queued. Submit a new request.");
        IDatabase? provider = server.DbType switch
        {
            DatabaseType.MySql => new MySqlDatabase(server, tunnel!, logger, crypto, configuration["DatabaseTools:MySqlDumpPath"]),
            DatabaseType.PostgreSql => new PostgreSqlDatabase(server, tunnel!, logger, crypto, configuration["DatabaseTools:PostgresDumpPath"]),
            DatabaseType.SqlServer => new SqlServerDatabase(server, tunnel!, logger, crypto, configuration["SqlServer:RemoteBackupRoot"], configuration["SqlServer:LocalStagingDirectory"]),
            DatabaseType.Sqlite => null,
            _ => throw new BackupQueueException(400, "Unsupported database provider.")
        };
        var config = await db.Configurations.FirstOrDefaultAsync(c => c.ServerId == server.Id, token);
        if (config is null)
        {
            config = new ServerBackupsConfiguration { ServerId = server.Id, TimeInDaysToHoldBackups = settings.Current.DefaultRetentionDays };
            db.Configurations.Add(config);
            await db.SaveChangesAsync(token);
        }
        var path = server.DbType == DatabaseType.Sqlite
            ? await SqliteBackup.Export(server, config, configuration, token)
            : config.CreateBackupPath(server, await provider!.PerformBackup(config, token)).FullFilePath!;
        var output = server.DbType is DatabaseType.SqlServer or DatabaseType.Sqlite ? path : Path.ChangeExtension(path, ".zip");
        var outputCreated = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (server.DbType is not (DatabaseType.SqlServer or DatabaseType.Sqlite))
            {
                await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read))
                await using (var destination = Modules.Backup.Core.StaticClasses.SafeBackupPath.CreatePrivateFile(output))
                {
                    outputCreated = true;
                    using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
                    await using var entry = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Optimal).Open();
                    await source.CopyToAsync(entry, token);
                }
                File.Delete(path);
            }
            token.ThrowIfCancellationRequested();
            var encrypted = await encryption.Encrypt(output, server.Id, token);
            return new(Guid.CreateVersion7(), $"{server.ServerHost} -> {server.ServerPort} -> {server.DbName}", encrypted.Path, encrypted.KeyId);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            if (output != path && outputCreated && File.Exists(output)) File.Delete(output);
            throw;
        }
    }
}
