using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Crypto.Shared.Interfaces;
namespace Modules.Backup.Core.Entities.Databases;
public sealed class SqlServerDatabase(DbServerConnection connection, DbServerTunnel tunnel, ILogger logger, ICryptoService crypto)
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
        await using var command = new SqlCommand($"BACKUP DATABASE [{name}] TO DISK = @path WITH INIT, CHECKSUM", sql) { CommandTimeout = 1800 };
        command.Parameters.AddWithValue("@path", path);
        await command.ExecuteNonQueryAsync(token);
        if (!File.Exists(path)) throw new InvalidOperationException("SQL Server backup is not visible locally. Configure a shared backup volume.");
    }
}
