using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Npgsql;
using MySqlConnector;

namespace Modules.Backup.Application.Static;

public static class DatabaseConnectionErrors
{
    // Only fixed categories cross the API/log boundary. Exception text can contain
    // usernames, connection strings, certificates, SQL, paths and secrets.
    public static string Describe(Exception error)
    {
        if (error is CryptographicException or FormatException)
            return "Saved credentials could not be decrypted. Restore the original Data Protection key ring (and CRYPTO_KEY for legacy credentials), or re-enter this connection's password. No key was replaced.";
        if (error is PostgresException pg)
            return pg.SqlState switch {
                "28P01" => "PostgreSQL rejected authentication. Re-enter the database username/password and check pg_hba.conf for the API's client address.",
                "28000" => "PostgreSQL rejected this client or transport. Check pg_hba.conf, the database role and the selected TLS policy.",
                "3D000" => "The configured PostgreSQL database does not exist.",
                "42501" => "The PostgreSQL account lacks permission to connect or query this database.",
                "53300" => "PostgreSQL has no available connection slots. Check server load and connection limits.",
                _ => "PostgreSQL rejected the connection check. Ask its administrator to inspect the server log using the connection time." };
        if (error is MySqlException mysql)
            return mysql.Number switch { 1045 => "MySQL/MariaDB rejected authentication. Check the saved credentials and the account's allowed client hosts.",
                1049 => "The configured MySQL/MariaDB database does not exist.",
                _ => Inner(error) ?? "MySQL/MariaDB connection failed. Check the database address, credentials and TLS policy." };
        if (error is SqlException sql)
            return sql.Number switch { 18456 => "SQL Server rejected authentication. Check SQL authentication, credentials and the login's database access.",
                4060 => "SQL Server could not open the configured database for this login.",
                _ => Inner(error) ?? "SQL Server connection failed. Check TCP/IP, the actual listening port, credentials and certificate trust." };
        return Inner(error) ?? "Connection failed. Check the database address, transport policy and SSH host-key pins.";
    }

    private static string? Inner(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException) return "TLS negotiation or certificate verification failed. Check the server certificate, hostname and CA trust. Verified TLS was not disabled.";
            if (current is TimeoutException or OperationCanceledException) return "The database connection timed out. Check routing, firewall, listening port and SSH tunnel access.";
            if (current is SocketException socket) return socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                ? "The database hostname could not be resolved. Docker service names require the API to share that Docker network; a host-run API needs a reachable address and published port."
                : "The database TCP endpoint is unreachable or refused the connection. Check listening address, container network, TCP port and firewall.";
        }
        // Provider messages are inspected only to select fixed safe hints.
        if (error.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            return "TLS negotiation failed. Check server TLS support, certificate hostname and CA trust. The selected TLS policy was not changed.";
        return null;
    }
}
