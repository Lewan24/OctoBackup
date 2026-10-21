using Microsoft.Extensions.Logging;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Crypto.Shared.Interfaces;
namespace Modules.Backup.Core.Entities.Databases;
public sealed class MySqlDatabase(DbServerConnection connection, DbServerTunnel tunnel, ILogger logger, ICryptoService crypto)
    : DatabaseBase(connection, tunnel, logger, ".sql", crypto)
{
    protected override async Task PerformBackupInternal(string path, CancellationToken token)
    {
        var (host, port) = GetHostAndPort();
        var client = await MySqlDumpClient.Find(token);
        var arguments = client.Arguments(host, port, GetServerConnection().DbUser, GetServerConnection().DbName, GetServerConnection().SsL);
        await DumpProcess.Run(client.Executable, arguments,
            new Dictionary<string, string?> { ["MYSQL_PWD"] = Crypto.Decrypt(GetServerConnection().DbPasswd) }, path, cancellationToken: token);
    }
}
