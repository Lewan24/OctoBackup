using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;
using Modules.Crypto.Shared.Interfaces;
using MySqlConnector;
using Npgsql;
using Renci.SshNet;

namespace Modules.Backup.Application.Static;

internal static class ServerConnectionTester
{
    public static async Task<(bool Result, string? ErrorMsg)> TestConnectionAsync(
        DbServerConnection conn,
        DbServerTunnel? tunnel,
        ICryptoService cryptoService)
    {
        SshClient? sshClient = null;
        ForwardedPortLocal? portForward = null;

        try
        {
            var host = conn.ServerHost;
            int port = conn.ServerPort;

            if (conn.IsTunnelRequired && tunnel is not null)
            {
                AuthenticationMethod auth;
                if (tunnel.UsePasswordAuth)
                {
                    var decryptedPassword = cryptoService.Decrypt(tunnel.Password);
                    auth = new PasswordAuthenticationMethod(tunnel.Username, decryptedPassword);
                }
                else
                {
                    var decryptedPem = cryptoService.Decrypt(tunnel.PrivateKeyContent);
                    var decryptedPemPassphrase = cryptoService.Decrypt(tunnel.PrivateKeyPassphrase);

                    using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(decryptedPem ?? ""));
                    auth = !string.IsNullOrEmpty(decryptedPemPassphrase)
                        ? new PrivateKeyAuthenticationMethod(
                            tunnel.Username,
                            new PrivateKeyFile(keyStream, decryptedPemPassphrase))
                        : new PrivateKeyAuthenticationMethod(
                            tunnel.Username,
                            new PrivateKeyFile(keyStream));
                }

                var connectionInfo = new ConnectionInfo(
                    tunnel.ServerHost,
                    tunnel.SshPort,
                    tunnel.Username,
                    auth);

                sshClient = new SshClient(connectionInfo);
                sshClient.Connect();

                portForward = new ForwardedPortLocal("127.0.0.1",
                    (uint)tunnel.LocalPort,
                    tunnel.RemoteHost,
                    (uint)tunnel.RemotePort);
                sshClient.AddForwardedPort(portForward);
                portForward.Start();

                host = "127.0.0.1";
                port = tunnel.LocalPort;
            }

            var decryptedDbPasswd = cryptoService.Decrypt(conn.DbPasswd);

            IDbConnection dbConn = conn.DbType switch
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
                        SslMode = conn.SsL ? MySqlSslMode.Required : MySqlSslMode.None,
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
                        SslMode = conn.SsL ? SslMode.Require : SslMode.Disable,
                    }.ToString()),

                DatabaseType.SqlServer => new SqlConnection(
                    new SqlConnectionStringBuilder
                    {
                        DataSource = $"{host},{port}",
                        InitialCatalog = conn.DbName,
                        UserID = conn.DbUser,
                        Password = decryptedDbPasswd,
                        ConnectTimeout = 5,
                        TrustServerCertificate = conn.SsL
                    }.ToString()),

                _ => throw new NotSupportedException($"Unsupported DB type: {conn.DbType}")
            };

            await OpenAsync(dbConn);

            var isOk = await ExecuteTestQueryAsync(dbConn, conn.DbType);
            return (isOk, null);
        }
        catch (Exception ex)
        {
            var errorMsg = $"❌ Connection failed: {ex.Message}";
            Console.WriteLine(errorMsg);
            return (false, errorMsg);
        }
        finally
        {
            portForward?.Stop();
            if (sshClient?.IsConnected == true)
                sshClient.Disconnect();
            sshClient?.Dispose();
        }
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