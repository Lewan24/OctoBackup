using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;

if (args.FirstOrDefault() == "compatibility")
{
    await CompatibilitySmoke.Run(args.Skip(1).ToArray());
    return;
}

// Generates only disposable fixtures. Never connects to a source database.
var type = args.FirstOrDefault() switch { "mysql" => DatabaseType.MySql, "postgres" => DatabaseType.PostgreSql,
    "sqlserver" => DatabaseType.SqlServer, _ => throw new ArgumentException("Use mysql, postgres, or sqlserver <disposable-native-bak>.") };
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:Enabled"] = "true" }).Build();
var runner = new DockerRestoreTestRunner(config, new DockerRestoreContainerCommands());
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var path = type == DatabaseType.SqlServer ? Path.GetFullPath(args.ElementAtOrDefault(1) ?? throw new ArgumentException("Supply a disposable native .bak with a probe table containing verified-row."))
    : Path.Combine(Path.GetTempPath(), "octobackup-restore-smoke-" + Guid.NewGuid() + ".zip");
try
{
    if (type != DatabaseType.SqlServer)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("probe.sql").Open());
        await writer.WriteAsync("CREATE TABLE probe (id int PRIMARY KEY, value varchar(100)); INSERT INTO probe VALUES (1,'verified-row'); CREATE TABLE empty_probe (id int);");
    }
    var test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = type, DatabaseName = "restore_probe" };
    try
    {
        var preview = await runner.Run(test, path, timeout.Token);
        if (!preview.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Contains("verified-row"))
            throw new InvalidOperationException("Restored representative row did not match.");
        Console.WriteLine($"{type}: restore passed, {preview.TableCount} user tables, representative row matched.");
    }
    finally { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await runner.Cleanup(test.Id, cleanup.Token); }
    if (type != DatabaseType.SqlServer)
    {
        File.Delete(path);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("broken.sql").Open()); writer.Write("THIS IS NOT VALID SQL;"); }
        test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = type };
        try
        {
            try { await runner.Run(test, path, timeout.Token); throw new InvalidOperationException("Invalid SQL incorrectly passed."); }
            catch (RestoreTestException) { Console.WriteLine($"{type}: corrupt SQL correctly rejected."); }
        }
        finally { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await runner.Cleanup(test.Id, cleanup.Token); }
    }
}
finally { if (type != DatabaseType.SqlServer && File.Exists(path)) File.Delete(path); }
