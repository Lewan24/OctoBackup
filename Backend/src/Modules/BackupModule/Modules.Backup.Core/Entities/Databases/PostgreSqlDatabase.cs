using Microsoft.Extensions.Logging;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Crypto.Shared.Interfaces;
namespace Modules.Backup.Core.Entities.Databases;
public sealed class PostgreSqlDatabase(DbServerConnection connection, DbServerTunnel tunnel, ILogger logger, ICryptoService crypto, string? dumpExecutable = null)
    : DatabaseBase(connection, tunnel, logger, ".sql", crypto)
{
    protected override Task PerformBackupInternal(string path, CancellationToken token)
    {
        var (host, port) = GetHostAndPort();
        return DumpProcess.Run(DumpToolPaths.Postgres(dumpExecutable), ["--host=" + host, "--port=" + port, "--username=" + GetServerConnection().DbUser, "--no-password", "--no-owner", "--no-privileges", "--dbname=" + GetServerConnection().DbName],
            new Dictionary<string, string?> { ["PGPASSWORD"] = Crypto.Decrypt(GetServerConnection().DbPasswd), ["PGGSSENCMODE"] = "disable", ["PGSSLMODE"] = GetServerConnection().SsL ? "verify-full" : "disable" }, path, cancellationToken: token);
    }
}
