using Modules.Backup.Core.Entities.DbContext;

namespace Modules.Backup.Core.Interfaces;

public interface IDatabase
{
    /// <summary>
    ///     Create a new backup for specified database
    /// </summary>
    /// <param name="serverConfig">Provided <see cref="ServerBackupsConfiguration" /></param>
    /// <returns>Returns created backup file name</returns>
    Task<string> PerformBackup(ServerBackupsConfiguration serverConfig, CancellationToken token = default);

    string GetDatabaseName();
    Guid GetServerId();
    DbServerConnection GetServerConnection();
    string GetBackupExtension();
}
