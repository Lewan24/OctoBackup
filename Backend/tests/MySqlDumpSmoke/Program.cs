using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Backup.Core.Entities.Databases;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Crypto.Shared.Interfaces;
using Modules.Shared.Common;

namespace OctoBackup.MySqlDumpSmoke;

public static class EntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        // Reserved for disposable local schemas, never an application's live data.
        var port = ushort.Parse(args[0]);
        var password = Environment.GetEnvironmentVariable("MYSQL_PWD") ?? throw new InvalidOperationException("Set the disposable account's MYSQL_PWD.");
        var data = Path.GetFullPath("Backend/artifacts/mysql-dump-smoke");
        DbCommon.Configure(data, null);
        var connection = new DbServerConnection
        {
            ConnectionName = "Disposable MySQL dump check", ServerHost = "127.0.0.1", ServerPort = port,
            DbUser = "backup", DbPasswd = password, DbName = "backup_probe", SsL = false
        };
        var provider = new MySqlDatabase(connection, null!, NullLogger.Instance, new FixtureCrypto());
        var configuration = new ServerBackupsConfiguration { ServerId = connection.Id };
        try
        {
            var filename = await provider.PerformBackup(configuration);
            if (args.Contains("--expect-denied")) throw new InvalidOperationException("Expected routine-visibility failure did not occur.");
            var path = configuration.CreateBackupPath(connection, filename).FullFilePath!;
            var text = await File.ReadAllTextAsync(path);
            if (!text.Contains("verified-row") || !text.Contains("get_probe")) throw new InvalidOperationException("The dump omitted expected data or routine definitions.");
            Console.WriteLine(JsonSerializer.Serialize(new { path, bytes = new FileInfo(path).Length }));
            return 0;
        }
        catch (DumpFailureException failure) when (args.Contains("--expect-denied"))
        {
            if (!failure.SafeMessage.Contains("SHOW_ROUTINE")) throw;
            Console.WriteLine("Expected routine-visibility failure: safe actionable error; partial output removed.");
            return 0;
        }
    }
    private sealed class FixtureCrypto : ICryptoService
    {
        public string? Encrypt(string? value) => value;
        public string? Decrypt(string? value) => value;
    }
}
