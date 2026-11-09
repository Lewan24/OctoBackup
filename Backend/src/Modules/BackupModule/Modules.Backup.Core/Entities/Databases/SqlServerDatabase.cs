using Microsoft.Data.SqlClient;
using Modules.Backup.Core.StaticClasses;
using Microsoft.Extensions.Logging;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Crypto.Shared.Interfaces;
namespace Modules.Backup.Core.Entities.Databases;
public sealed class SqlServerDatabase(DbServerConnection connection, DbServerTunnel tunnel, ILogger logger, ICryptoService crypto, string? remoteBackupRoot = null, string? localStagingDirectory = null)
    : DatabaseBase(connection, tunnel, logger, ".bak", crypto)
{
    protected override async Task PerformBackupInternal(string path, CancellationToken token)
    {
        var (host, port) = GetHostAndPort();
        var builder = new SqlConnectionStringBuilder { DataSource = $"{host},{port}", InitialCatalog = GetServerConnection().DbName,
            UserID = GetServerConnection().DbUser, Password = Crypto.Decrypt(GetServerConnection().DbPasswd), TrustServerCertificate = false,
            Encrypt = GetServerConnection().SsL ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional, ConnectTimeout = 15 };
        await using var sql = new SqlConnection(builder.ConnectionString);
        await sql.OpenAsync(token);
        var name = GetServerConnection().DbName.Replace("]", "]]", StringComparison.Ordinal);
        await using var command = new SqlCommand($"BACKUP DATABASE [{name}] TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM", sql) { CommandTimeout = 1800 };
        var storage = SqlServerBackupStorage.Resolve(path, remoteBackupRoot, localStagingDirectory);
        command.Parameters.AddWithValue("@path", storage.RemotePath);
        try
        {
            await command.ExecuteNonQueryAsync(token);
            SqlServerBackupStorage.CheckLinks(storage.LocalPath);
            if (!File.Exists(storage.LocalPath)) throw SqlServerBackupStorage.Failure(
                "SQL Server created its backup, but the API cannot read that file. Mount the same staging directory in SQL Server and the API, configure SqlServer:LocalStagingDirectory and SqlServer:RemoteBackupRoot, and grant both service accounts access. TCP access alone cannot retrieve a native .bak.");
            if (storage.CopyRequired)
            {
                await using var source = File.OpenRead(storage.LocalPath);
                await using var destination = SafeBackupPath.CreatePrivateFile(path);
                await source.CopyToAsync(destination, token);
                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }
        }
        catch (SqlException error) when (error.Number is 3201 or 3013 or 3271)
        {
            throw SqlServerBackupStorage.Failure("SQL Server could not write the native backup. Check the shared staging mount, the SQL Server service account's write permissions and free disk space. The SQL login's BACKUP permission does not grant filesystem access.");
        }
        catch (UnauthorizedAccessException)
        {
            throw SqlServerBackupStorage.Failure("The API cannot read or remove the SQL Server staging file. Grant the API account read/delete access to the dedicated shared staging directory.");
        }
        finally
        {
            // Delete only the generated staging filename, never arbitrary remote files.
            if (storage.CopyRequired)
            {
                SqlServerBackupStorage.CheckLinks(storage.LocalPath);
                try { if (File.Exists(storage.LocalPath)) File.Delete(storage.LocalPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    throw SqlServerBackupStorage.Failure("The native backup staging file could not be removed. Grant the API delete access to the shared staging directory and inspect leftover plaintext files before retrying.");
                }
            }
        }
    }
}
