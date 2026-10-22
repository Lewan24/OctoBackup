using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

public static class BackupTarget
{
    // Credentials, TLS preferences and display names do not make a new database.
    // Alias hostnames resolving to the same physical machine remain an operator concern.
    public static string Key(DbServerConnection server, DbServerTunnel? tunnel)
    {
        var values = new object?[] { server.DbType, DatabaseHostPolicy.Normalize(server.ServerHost), server.ServerPort, server.DbName,
            server.IsTunnelRequired ? DatabaseHostPolicy.Normalize(tunnel?.ServerHost) : null,
            server.IsTunnelRequired ? tunnel?.SshPort : null,
            server.IsTunnelRequired ? DatabaseHostPolicy.Normalize(tunnel?.RemoteHost) : null,
            server.IsTunnelRequired ? tunnel?.RemotePort : null };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    }
}
