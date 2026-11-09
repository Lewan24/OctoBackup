using Modules.Backup.Core.StaticClasses;
using System.Data;
using Microsoft.Data.SqlClient;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;
using Modules.Crypto.Shared.Interfaces;
using MySqlConnector;
using Npgsql;

namespace Modules.Backup.Application.Static;

public static class ServerConnectionTester
{
    public static async Task<(bool Result, string? ErrorMsg)> TestConnectionAsync(
        DbServerConnection conn,
        DbServerTunnel? tunnel,
        ICryptoService cryptoService, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        IDisposable? tunnelLease = null;
        try
        {
            if (conn.DbType == DatabaseType.Sqlite)
            {
                var path = Modules.Backup.Application.Services.SqliteBackup.Source(configuration, conn.DbName, conn.IsTunnelRequired);
                await using var sqlite = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                await sqlite.OpenAsync();
                await using var command = sqlite.CreateCommand();
                command.CommandText = "SELECT count(*) FROM sqlite_schema";
                await command.ExecuteScalarAsync();
                return (true, null);
            }
            // Match the export path, including disposal of the private-key stream.
            if (conn.IsTunnelRequired && tunnel is null) return (false, "The configured SSH tunnel is missing.");
            tunnelLease = conn.IsTunnelRequired ? SshTunnelHelper.OpenTunnel(conn, tunnel!, cryptoService) : null;
            var host = conn.IsTunnelRequired ? "127.0.0.1" : conn.ServerHost;
            var port = conn.IsTunnelRequired ? tunnel!.LocalPort : conn.ServerPort;

            var decryptedDbPasswd = cryptoService.Decrypt(conn.DbPasswd);

            using IDbConnection dbConn = conn.DbType switch
            {
                DatabaseType.MySql => new MySqlConnection(
                    new MySqlConnectionStringBuilder
                    {
                        Server = host,
                        Port = (uint)port,
                        Database = conn.DbName,
                        UserID = conn.DbUser,
                        Password = decryptedDbPasswd,
                        ConnectionTimeout = 5,
                        SslMode = conn.SsL ? MySqlSslMode.VerifyFull : MySqlSslMode.None,
                        // Explicit TLS opt-out is intended for local development targets.
                        AllowPublicKeyRetrieval = !conn.SsL,
                    }.ToString()),

                DatabaseType.PostgreSql => new NpgsqlConnection(
                    new NpgsqlConnectionStringBuilder
                    {
                        Host = host,
                        Port = port,
                        Database = conn.DbName,
                        Username = conn.DbUser,
                        Password = decryptedDbPasswd,
                        Timeout = 5,
                        CommandTimeout = 5,
                        SslMode = conn.SsL ? SslMode.VerifyFull : SslMode.Disable,
                        GssEncryptionMode = GssEncryptionMode.Disable,
                    }.ToString()),

                DatabaseType.SqlServer => new SqlConnection(
                    new SqlConnectionStringBuilder
                    {
                        DataSource = $"{host},{port}",
                        InitialCatalog = conn.DbName,
                        UserID = conn.DbUser,
                        Password = decryptedDbPasswd,
                        ConnectTimeout = 5,
                        TrustServerCertificate = false,
                        Encrypt = conn.SsL ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional
                    }.ToString()),

                _ => throw new NotSupportedException($"Unsupported DB type: {conn.DbType}")
            };

            await OpenAsync(dbConn);

            var isOk = await ExecuteTestQueryAsync(dbConn, conn.DbType);
            return (isOk, null);
        }
        catch (Exception error) { return (false, DatabaseConnectionErrors.Describe(error)); }
        finally { tunnelLease?.Dispose(); }
    }

    private static async Task OpenAsync(IDbConnection conn)
    {
        switch (conn)
        {
            case MySqlConnection my:
                await my.OpenAsync();
                break;
            case NpgsqlConnection npg:
                await npg.OpenAsync();
                break;
            case SqlConnection sql:
                await sql.OpenAsync();
                break;
            default:
                conn.Open();
                break;
        }
    }

    private static async Task<bool> ExecuteTestQueryAsync(IDbConnection conn, DatabaseType type)
    {
        var testQuery = type switch
        {
            DatabaseType.MySql => "SELECT 1;",
            DatabaseType.PostgreSql => "SELECT 1;",
            DatabaseType.SqlServer => "SELECT 1;",
            _ => throw new NotSupportedException()
        };

        switch (conn)
        {
            case MySqlConnection my:
                await using (var cmd = new MySqlCommand(testQuery, my))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    return result is not null;
                }

            case NpgsqlConnection npg:
                await using (var cmd = new NpgsqlCommand(testQuery, npg))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    return result is not null;
                }

            case SqlConnection sql:
                await using (var cmd = new SqlCommand(testQuery, sql))
                {
                    var result = await cmd.ExecuteScalarAsync();
                    return result is not null;
                }

            default:
                return false;
        }
    }
}
