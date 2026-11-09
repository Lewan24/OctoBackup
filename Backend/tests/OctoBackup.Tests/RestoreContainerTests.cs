using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;
using Xunit;

public sealed class ContainerCommandsFixture : IRestoreContainerCommands
{
    public List<string[]> Commands { get; } = [];
    public string? RestoredSql { get; private set; }
    public string PgVersion { get; set; } = "170011";
    private string _image = "";
    public string Catalog { get; set; } = "{\"schema\":\"public\",\"name\":\"odd'`table\"}\n";
    public async Task<string> Run(IEnumerable<string> arguments, Dictionary<string, string?>? env, Stream? input, CancellationToken token)
    {
        var args = arguments.ToArray(); Commands.Add(args);
        if (args[0] == "run") _image = args.Last();
        if (input is not null) { using var reader = new StreamReader(input, leaveOpen: true); RestoredSql = await reader.ReadToEndAsync(token); }
        var query = args.Last();
        if (query.Contains("SELECT VERSION()")) return _image.Contains("5.7") ? "5.7.44" : _image.Contains("mariadb") ? "10.11.14-MariaDB" : "8.4.8";
        if (query.Contains("current_setting('server_version_num')")) return PgVersion;
        if (query.Contains("RESTORE FILELISTONLY")) return "data'name|C:\\production\\private.mdf|D|PRIMARY|32|1|1\nlog|C:\\production\\private.ldf|L||32|1|2\n";
        if (query.Contains("FROM sys.tables WHERE")) return "[{\"schema\":\"dbo\",\"name\":\"probe\"}]";
        if (query.Contains("FROM sys.columns")) return "[{\"name\":\"value\"}]";
        if (query.Contains("SELECT TOP(20)")) return "[{\"value\":\"verified-row\"}]";
        if (query.Contains("pg_catalog.pg_tables") || query.Contains("information_schema.tables")) return Catalog;
        if (query.Contains("information_schema.columns")) return "{\"name\":\"value\"}\n";
        if (query.Contains("row_to_json") || query.StartsWith("SELECT JSON_OBJECT(CONVERT")) return "{\"value\":\"verified-row\"}\n";
        return "";
    }
}

public sealed class RestoreContainerTests
{
    [Fact]
    public async Task MariaDb_UsesItsOwnClientAndWritableSocketDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("probe.sql").Open()); writer.Write("-- MariaDB dump 10.19\n-- Server version 10.11.14-MariaDB\nCREATE TABLE probe(value text);"); }
            var commands = new ContainerCommandsFixture();
            await new DockerRestoreTestRunner(new ConfigurationBuilder().Build(), commands).Run(
                new BackupTest { Id = Guid.NewGuid(), DatabaseType = DatabaseType.MySql, DatabaseName = "compat_data" }, path, CancellationToken.None);
            Assert.Contains("mariadb:10.11", commands.Commands[0]);
            Assert.Contains("/run/mysqld:rw,nosuid,nodev,size=16m,mode=0755", commands.Commands[0]);
            Assert.Contains(commands.Commands, c => c.Contains("mariadb") && c.Contains("--binary-mode") && c.Contains("--skip-ssl"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("170011", true)]
    [InlineData("160012", false)]
    public async Task PostgreSql17Settings_AreRestoredUnchanged_AndAnOlderImageIsRejectedBeforeSql(string version, bool succeeds)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        const string sql = "-- PostgreSQL database dump\n-- Dumped from database version 17.11\n-- Dumped by pg_dump version 17.6\nSET transaction_timeout = 0;\nCREATE TABLE probe(value text);";
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("probe.sql").Open()); writer.Write(sql); }
            var commands = new ContainerCommandsFixture { PgVersion = version };
            var runner = new DockerRestoreTestRunner(new ConfigurationBuilder().Build(), commands);
            var test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = DatabaseType.PostgreSql };
            if (succeeds)
            {
                await runner.Run(test, path, CancellationToken.None);
                Assert.Equal(sql, commands.RestoredSql);
                Assert.Contains("postgres:17", commands.Commands[0]);
            }
            else
            {
                var error = await Assert.ThrowsAsync<RestoreTestException>(() => runner.Run(test, path, CancellationToken.None));
                Assert.Contains("Checking database version", error.Message); Assert.Null(commands.RestoredSql);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MySqlDatabaseArchives_AreInspectedInTheirOriginalDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("probe.sql").Open()); writer.Write("-- MySQL dump 10.13\n-- Server version 5.7.44\n-- Current Database: `original_db`\nCREATE DATABASE IF NOT EXISTS original_db; USE original_db; CREATE TABLE probe(value text);"); }
            var commands = new ContainerCommandsFixture();
            await new DockerRestoreTestRunner(new ConfigurationBuilder().Build(), commands).Run(
                new BackupTest { Id = Guid.NewGuid(), DatabaseType = DatabaseType.MySql, DatabaseName = "metadata_db" }, path, CancellationToken.None);
            Assert.Contains("mysql:5.7", commands.Commands[0]); Assert.Contains("MYSQL_DATABASE=original_db", commands.Commands[0]);
            Assert.All(commands.Commands.Where(c => c.Contains("mysql")), c => Assert.Contains("--database=original_db", c));
            Assert.Contains(commands.Commands, c => c.Last().Contains("table_schema=CONVERT(0x6F726967696E616C5F6462"));
            Assert.Contains("USE original_db;", commands.RestoredSql);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("No such image: private-image SECRET", "not preloaded")]
    [InlineData("error during connect: SECRET", "engine is unavailable")]
    [InlineData("permission denied SECRET", "runtime permissions")]
    [InlineData("read-only file system SECRET", "writable path")]
    [InlineData("role SECRET does not exist", "role missing")]
    [InlineData("unrecognized configuration parameter SECRET", "dump requires")]
    [InlineData("ERROR 1273 (HY000) at line 42: Unknown collation SECRET", "collation")]
    public void RuntimeErrors_ProduceActionableFixedMessages_WithoutRawOutput(string raw, string expected)
    {
        var message = DockerRestoreContainerCommands.FailureMessage("run", 125, raw);
        Assert.Contains(expected, message);
        Assert.Contains("exit 125", message);
        Assert.DoesNotContain("SECRET", message);
    }

    [Fact]
    public async Task NativeRestore_VerifiesAndMovesAllFiles_WithoutReusingPhysicalPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "restore-" + Guid.NewGuid() + ".bak");
        try
        {
            await File.WriteAllTextAsync(path, "fixture native artifact");
            var commands = new ContainerCommandsFixture();
            var runner = new DockerRestoreTestRunner(new ConfigurationBuilder().Build(), commands);
            var preview = await runner.Run(new BackupTest { Id = Guid.NewGuid(), DatabaseType = DatabaseType.SqlServer }, path, CancellationToken.None);
            Assert.Equal("verified-row", Assert.Single(Assert.Single(preview.Tables).Rows)[0]);
            var start = commands.Commands[0];
            Assert.Contains("--cap-drop=ALL",start); Assert.Contains("--network=none",start);
            Assert.Contains("--security-opt=no-new-privileges:true",start);
            Assert.Equal("--cap-add=NET_BIND_SERVICE",Assert.Single(start,a=>a.StartsWith("--cap-add=")));
            var restore = Assert.Single(commands.Commands, c => c.Last().Contains("RESTORE DATABASE")).Last();
            Assert.Contains("RESTORE VERIFYONLY", restore);
            Assert.Contains("MOVE N'data''name' TO N'/var/opt/mssql/data/probe_0.mdf'", restore);
            Assert.Contains("MOVE N'log' TO N'/var/opt/mssql/data/probe_1.ldf'", restore);
            Assert.DoesNotContain("production", restore);
            Assert.DoesNotContain(commands.Commands, c => c[0] == "cp" || c.Contains("--user=0"));
            Assert.Contains(commands.Commands, c=>c.Last()=="umask 077; cat > /var/opt/mssql/probe.bak");
            Assert.Equal("fixture native artifact",commands.RestoredSql);
            Assert.Contains(commands.Commands, c => c.Contains("-b"));
            var query = commands.Commands.First(c=>c.Contains("/opt/mssql-tools18/bin/sqlcmd"));
            Assert.DoesNotContain("-h",query); Assert.DoesNotContain("-W",query);
            Assert.Contains("-y",query); Assert.Contains("tcp:127.0.0.1,1433",query);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReloadedRuntime_AffectsNextScope_AndCannotPreventInFlightCleanup()
    {
        var path=Path.Combine(Path.GetTempPath(),"restore-reload-"+Guid.NewGuid()+".zip");
        try
        {
            using(var archive=ZipFile.Open(path,ZipArchiveMode.Create))
            { using var writer=new StreamWriter(archive.CreateEntry("probe.sql").Open()); writer.Write("CREATE TABLE probe(id int);"); }
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["RestoreTests:Enabled"]="true",["RestoreTests:PostgresImage"]="fixture/postgres:original",["RestoreTests:MemoryMiB"]="4096" }).Build();
            var commands=new ContainerCommandsFixture(); var runner=new DockerRestoreTestRunner(config,commands);
            config["RestoreTests:Enabled"]="false"; config["RestoreTests:PostgresImage"]="fixture/postgres:reloaded"; config["RestoreTests:MemoryMiB"]="8192";
            var test=new BackupTest { Id=Guid.NewGuid(),DatabaseType=DatabaseType.PostgreSql };
            await runner.Run(test,path,default);
            Assert.Contains("fixture/postgres:original",commands.Commands[0]); Assert.Contains("--memory=4096m",commands.Commands[0]);
            await runner.Cleanup(test.Id,default);
            Assert.Contains(commands.Commands,c=>c[0]=="ps");
            var nextCommands=new ContainerCommandsFixture(); var next=new DockerRestoreTestRunner(config,nextCommands);
            await next.Run(new BackupTest { Id=Guid.NewGuid(),DatabaseType=DatabaseType.PostgreSql },path,default);
            Assert.Contains("fixture/postgres:reloaded",nextCommands.Commands[0]); Assert.Contains("--memory=8192m",nextCommands.Commands[0]);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(DatabaseType.PostgreSql)]
    [InlineData(DatabaseType.MySql)]
    public async Task RestoreStreamsArchive_UsesIsolation_QuotesMetadata_AndReadsSamples(DatabaseType type)
    {
        var path = Path.Combine(Path.GetTempPath(), "restore-" + Guid.NewGuid() + ".zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            { using var writer = new StreamWriter(archive.CreateEntry("../../unused.sql").Open()); writer.Write("CREATE TABLE probe(id int);"); }
            var commands = new ContainerCommandsFixture();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:Enabled"] = "true" }).Build();
            var runner = new DockerRestoreTestRunner(config, commands);
            var test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = type };
            var preview = await runner.Run(test, path, CancellationToken.None);
            Assert.Equal("CREATE TABLE probe(id int);", commands.RestoredSql);
            Assert.Equal("verified-row", Assert.Single(Assert.Single(preview.Tables).Rows)[0]);
            Assert.Equal("odd'`table", Assert.Single(preview.Catalog).Name);
            var run = commands.Commands[0];
            foreach (var flag in new[] { "--network=none", "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges:true", "--pull=never", "--pids-limit=256" }) Assert.Contains(flag, run);
            Assert.DoesNotContain(run, a => a == "-v" || a.StartsWith("--publish"));
            Assert.DoesNotContain(run, a => a.Contains("Ob!"));
            if (type == DatabaseType.PostgreSql)
            {
                Assert.Contains(commands.Commands, c => c.Contains("--set=ON_ERROR_STOP=1"));
                Assert.Contains(commands.Commands, c => c.Last().Contains("odd''`table"));
            }
            else Assert.Contains(commands.Commands, c => c.Last().Contains("CONVERT(0x"));
            await runner.Cleanup(test.Id, CancellationToken.None);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ArchiveWithMultipleFiles_IsRejected_AndNoUserTablesFails()
    {
        var path = Path.Combine(Path.GetTempPath(), "restore-" + Guid.NewGuid() + ".zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            { archive.CreateEntry("one.sql"); archive.CreateEntry("two.sql"); }
            var config = new ConfigurationBuilder().Build(); var commands = new ContainerCommandsFixture();
            var runner = new DockerRestoreTestRunner(config, commands);
            var test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = DatabaseType.PostgreSql };
            await Assert.ThrowsAsync<RestoreTestException>(() => runner.Run(test, path, CancellationToken.None));
            Assert.Null(commands.RestoredSql);
            File.Delete(path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) archive.CreateEntry("one.sql");
            commands.Catalog = "";
            var error = await Assert.ThrowsAsync<RestoreTestException>(() => runner.Run(test, path, CancellationToken.None));
            Assert.Contains("no user tables", error.Message);
        }
        finally { File.Delete(path); }
    }
}
