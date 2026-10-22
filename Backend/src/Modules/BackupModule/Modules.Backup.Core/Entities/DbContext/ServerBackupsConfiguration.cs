using Modules.Backup.Core.StaticClasses;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modules.Backup.Core.Entities.DbContext;

public sealed class ServerBackupsConfiguration
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServerId { get; set; }
    public int TimeInDaysToHoldBackups { get; set; } = 4;

    [NotMapped] private string BackupSaveDirectory { get; } = GetBackupDirectory();

    private static string GetBackupDirectory()
        => SafeBackupPath.Root;

    /// <summary>
    ///     Create required directories for provided server and database
    /// </summary>
    /// <param name="server">
    ///     <see cref="DbServerConnection" />
    /// </param>
    /// <param name="fileName">Target backup filename</param>
    /// <returns>
    ///     Returns base directory path for backup and full path with file name included. Full path will be null if file
    ///     name is not provided
    /// </returns>
    public (string DirectoryPath, string? FullFilePath) CreateBackupPath(DbServerConnection server, string? fileName)
    {
        if (!Directory.Exists(BackupSaveDirectory))
            Directory.CreateDirectory(BackupSaveDirectory);

        var serverPath = Path.Combine(BackupSaveDirectory, $"{SafeBackupPath.Segment(server.ServerHost)}_{server.ServerPort}");
        if (!Directory.Exists(serverPath))
            Directory.CreateDirectory(serverPath);

        var dbPath = Path.Combine(serverPath, SafeBackupPath.Segment(server.DbName));
        if (!Directory.Exists(dbPath))
            Directory.CreateDirectory(dbPath);

        if (!SafeBackupPath.IsWithinRoot(Path.Combine(dbPath, "probe"))) throw new InvalidOperationException("Backup directory contains an unsafe path or link.");
        return (dbPath, string.IsNullOrWhiteSpace(fileName) ? null : Path.Combine(dbPath, SafeBackupPath.Segment(fileName)));
    }
}
