using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Shared.Dtos;
using Modules.Shared.Common;

namespace Modules.Backup.Application.Services;

public static class SqliteBackup
{
    // Only a basename inside a deployment-owned source mount is accepted.
    public static string Source(IConfiguration config, string name, bool tunnel)
    {
        if (tunnel || string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new BackupQueueException(400, "SQLite requires a source filename in the configured local mount, without SSH.");
        var configured = config["Sqlite:SourceDirectory"];
        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
            throw new BackupQueueException(400, "SQLite source storage is not configured on this deployment.");
        var root = Path.GetFullPath(configured);
        var path = Path.GetFullPath(Path.Combine(root, name));
        // Do not allow the application's metadata/secrets to become downloadable.
        var data = Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(DbCommon.DbPath))!) + Path.DirectorySeparatorChar;
        if (path.StartsWith(data, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new BackupQueueException(400, "SQLite sources must be separate from application storage.");
        for (FileSystemInfo? current = new FileInfo(path); current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget is not null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new BackupQueueException(400, "SQLite sources must not contain symbolic links.");
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = new FileInfo(path + suffix);
            if (sidecar.LinkTarget is not null || (sidecar.Exists && sidecar.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new BackupQueueException(400, "SQLite source sidecars must not be symbolic links.");
        }
        if (!File.Exists(path)) throw new BackupQueueException(400, "The SQLite source file is unavailable.");
        return path;
    }

    private static SqliteConnection Connection(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder {
        DataSource = path, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 15 }.ToString());

    public static async Task<string> Export(DbServerConnection server, ServerBackupsConfiguration configuration, IConfiguration settings, CancellationToken token)
    {
        var sourcePath = Source(settings, server.DbName, server.IsTunnelRequired);
        var output = configuration.CreateBackupPath(server, $"{Guid.NewGuid():N}.sqlite").FullFilePath!;
        try
        {
            using (SafeBackupPath.CreatePrivateFile(output)) { }
            await using var source = Connection(sourcePath, SqliteOpenMode.ReadOnly);
            await source.OpenAsync(token);
            await using var destination = Connection(output, SqliteOpenMode.ReadWriteCreate);
            await destination.OpenAsync(token);
            // SQLite's snapshot API includes committed WAL content.
            token.ThrowIfCancellationRequested();
            source.BackupDatabase(destination);
            // The source can use WAL. Publish a self-contained snapshot whose
            // readers do not need/create sibling WAL/SHM files.
            await using var finalize = destination.CreateCommand();
            finalize.CommandText = "PRAGMA journal_mode=DELETE;";
            await finalize.ExecuteScalarAsync(token);
            token.ThrowIfCancellationRequested();
            return output;
        }
        catch { if (File.Exists(output)) File.Delete(output); throw; }
    }

    public static async Task<RestorePreview> Verify(string path, CancellationToken token)
    {
        await using var db = Connection(path, SqliteOpenMode.ReadOnly);
        await db.OpenAsync(token);
        db.EnableExtensions(false);
        await using var command = db.CreateCommand();
        command.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA query_only=ON; PRAGMA integrity_check(1);";
        command.CommandTimeout = 30;
        if (!string.Equals(Convert.ToString(await command.ExecuteScalarAsync(token)), "ok", StringComparison.Ordinal))
            throw new RestoreTestException("SQLite snapshot failed its integrity check.");
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1001";
        var names = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) names.Add(reader.GetString(0));
        if (names.Count > 1000) throw new RestoreTestException("SQLite snapshot exceeds the 1,000-table preview limit.");
        var samples = new List<RestoreTable>();
        foreach (var name in names.Take(5))
        {
            // Bound values in SQL before allocating managed strings/blobs.
            command.CommandText = "SELECT name FROM pragma_table_info(@table) ORDER BY cid LIMIT 32";
            command.Parameters.Clear(); command.Parameters.AddWithValue("@table", name);
            var columns = new List<string>();
            await using (var metadata = await command.ExecuteReaderAsync(token))
                while (await metadata.ReadAsync(token)) columns.Add(metadata.GetString(0));
            command.Parameters.Clear();
            static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            var projections = columns.Select(column => {
                var quoted = Quote(column);
                return $"CASE WHEN typeof({quoted})='blob' THEN '[binary: ' || length({quoted}) || ' bytes]' ELSE substr(CAST({quoted} AS TEXT),1,256) END";
            });
            command.CommandText = "SELECT " + string.Join(",", projections) + " FROM " + Quote(name) + " LIMIT 5";
            await using var reader = await command.ExecuteReaderAsync(token);
            var rows = new List<IReadOnlyList<string?>>();
            while (await reader.ReadAsync(token))
                rows.Add(Enumerable.Range(0, columns.Count).Select(i => reader.IsDBNull(i) ? null : reader.GetString(i)).ToArray());
            samples.Add(new("main", name, columns, rows));
        }
        return new(names.Count, samples, "SQLite snapshot reopened read-only and integrity-checked; first five tables/rows, 32 columns and 256 characters per value. This does not validate application behavior. Views and virtual tables may be unavailable with trusted_schema disabled.") {
            Catalog = names.Select(n => new RestoreTableName("main", n)).ToArray() };
    }
}
