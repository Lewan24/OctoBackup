using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Backup.Application.Services;
using Modules.Backup.Application.Static;
using Modules.Backup.Core.Entities.Databases;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.Interfaces;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Enums;
using Modules.Crypto.Application;
using Modules.Crypto.Shared.Interfaces;
using Modules.Shared.Common;

// This harness accepts only known disposable fixture names and synthetic accounts.
var type = Environment.GetEnvironmentVariable("FIXTURE_TYPE") switch { "mysql" => DatabaseType.MySql, "postgres" => DatabaseType.PostgreSql, "sqlserver" => DatabaseType.SqlServer, _ => throw new ArgumentException("Set FIXTURE_TYPE.") };
var host = Environment.GetEnvironmentVariable("FIXTURE_HOST") ?? throw new ArgumentException("Set FIXTURE_HOST.");
if (!host.StartsWith("octobackup-provider-", StringComparison.Ordinal) && !(host == "127.0.0.1" && Environment.GetEnvironmentVariable("FIXTURE_ALLOW_LOOPBACK") == "true")) throw new ArgumentException("Use a disposable octobackup-provider-* fixture host.");
var password = Environment.GetEnvironmentVariable("FIXTURE_PASSWORD") ?? throw new ArgumentException("Set synthetic FIXTURE_PASSWORD.");
var port = ushort.Parse(Environment.GetEnvironmentVariable("FIXTURE_PORT") ?? (type switch { DatabaseType.MySql => "3306", DatabaseType.PostgreSql => "5432", _ => "1433" }));
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("FIXTURE_DATA") ?? "/smoke-data");
DbCommon.Configure(root, null); DbCommon.CreateDbDirectoryIfNotExists();
var values = new Dictionary<string,string?> { ["CRYPTO_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
var tools = Environment.GetEnvironmentVariable("FIXTURE_MYSQL_DUMP");
var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
var services = new ServiceCollection().AddSingleton<IConfiguration>(config).AddCryptoApplication();
services.AddDataProtection().SetApplicationName("OctoBackup.ProviderSmoke").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root,"keys")));
using var provider = services.BuildServiceProvider();
var crypto = provider.GetRequiredService<ICryptoService>();
var server = new DbServerConnection { ServerHost = host, ServerPort = port, DbName = "backup_probe", DbUser = type switch { DatabaseType.MySql => "root", DatabaseType.PostgreSql => "postgres", _ => "sa" }, DbPasswd = crypto.Encrypt(password)!, DbType = type, SsL = false };
var check = await ServerConnectionTester.TestConnectionAsync(server,null,crypto,config);
if (!check.Result) throw new InvalidOperationException(check.ErrorMsg);
Console.WriteLine(type + ": saved-credential connection check passed.");
IDatabase database = type switch {
    DatabaseType.MySql => new MySqlDatabase(server,null!,NullLogger.Instance,crypto,tools),
    DatabaseType.PostgreSql => new PostgreSqlDatabase(server,null!,NullLogger.Instance,crypto),
    _ => new SqlServerDatabase(server,null!,NullLogger.Instance,crypto,Environment.GetEnvironmentVariable("FIXTURE_REMOTE_STAGE"),Environment.GetEnvironmentVariable("FIXTURE_LOCAL_STAGE")) };
var storage = new ServerBackupsConfiguration { ServerId = server.Id };
var name = await database.PerformBackup(storage);
var output = storage.CreateBackupPath(server,name).FullFilePath!;
if (type != DatabaseType.SqlServer)
{
    var zip = Path.ChangeExtension(output,".zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(output,Path.GetFileName(output));
    File.Delete(output); output = zip;
}
await using var db = new BackupsDbContext(); await db.Database.MigrateAsync();
var encryption = new BackupEncryptionService(db, provider.GetRequiredService<IDataProtectionProvider>(), NullLogger<BackupEncryptionService>.Instance);
await encryption.Initialize();
var administrator = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[] {
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role,"Admin") },"fixture"));
var customPassword = "Fixture!" + Guid.NewGuid().ToString("N");
await encryption.Change(null,new(null,null,customPassword,customPassword,Enabled:true),administrator,default);
var encrypted = await encryption.Encrypt(output,server.Id,default);
await using var plain = await encryption.OpenDecrypted(new PerformedBackup { Name="fixture",FilePath=encrypted.Path,EncryptionKeyId=encrypted.KeyId },null,false,default);
// Explicit synthetic restore fixture, never production backup data.
var recovered = Path.Combine(root,type == DatabaseType.SqlServer ? "fixture.bak" : "fixture.zip");
await using (var destination = File.Create(recovered)) await plain.CopyToAsync(destination);
Console.WriteLine(type + ": provider export, default encryption and authenticated recovery passed.");
await File.WriteAllTextAsync(Path.Combine(root,"result.json"),JsonSerializer.Serialize(new { type = type.ToString(), encrypted = encrypted.Path, recovered }));
