using Modules.Crypto.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.Interfaces;
using Modules.Backup.Core.StaticClasses;

namespace Modules.Backup.Core.Entities.Databases;

public abstract class DatabaseBase(
    DbServerConnection serverConnection,
    DbServerTunnel serverTunnel,
    ILogger logger,
    string? defaulBackupExtension, ICryptoService cryptoService) : IDatabase
{
    protected ICryptoService Crypto => cryptoService;
    protected string BackupExtension = defaulBackupExtension ?? ".sql";

    public async Task<string> PerformBackup(ServerBackupsConfiguration serverConfig, CancellationToken token = default)
    {
        logger.LogInformation("Performing backup for {DatabaseName}", serverConnection.DbName);

        var fileName = $"{DateTime.UtcNow:yyyy.MM.dd.HH.mm.ss.fff}_{Guid.NewGuid():N}{BackupExtension}";
        logger.LogInformation("Potential backup file name: {FileName}", fileName);

        var backupPath = serverConfig.CreateBackupPath(serverConnection, fileName);
        logger.LogInformation("Prepared backup path: {BackupPath}", backupPath.DirectoryPath);

        try
        {
            token.ThrowIfCancellationRequested();
            using var tunnel = serverConnection.IsTunnelRequired
                ? SshTunnelHelper.OpenTunnel(serverConnection, serverTunnel, cryptoService)
                : null;

            logger.LogInformation("Performing internal backup operation for file: {FullFilePath}",
                backupPath.FullFilePath);
            token.ThrowIfCancellationRequested();
            await PerformBackupInternal(backupPath.FullFilePath!, token);
            return fileName;
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException) logger.LogInformation("Backup cancelled for {DatabaseName}", serverConnection.DbName);
            else logger.LogError(e, "Error while performing backup for {DatabaseName}", serverConnection.DbName);

            try
            {
                if (File.Exists(backupPath.FullFilePath))
                    File.Delete(backupPath.FullFilePath);
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx, "Failed to clean up corrupted backup file {File}",
                    backupPath.FullFilePath);
            }

            throw;
        }
    }

    public string GetDatabaseName()
    {
        return serverConnection.DbName;
    }

    public Guid GetServerId()
    {
        return serverConnection.Id;
    }

    public DbServerConnection GetServerConnection()
    {
        return serverConnection;
    }

    public string GetBackupExtension()
    {
        return BackupExtension;
    }

    protected abstract Task PerformBackupInternal(string fullFilePath, CancellationToken token);

    protected (string Host, int Port) GetHostAndPort()
    {
        var host = serverConnection.IsTunnelRequired ? "127.0.0.1" : serverConnection.ServerHost;
        var port = serverConnection.IsTunnelRequired ? serverTunnel.LocalPort : serverConnection.ServerPort;

        return (host, port);
    }
}
