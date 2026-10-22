using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Shared.Enums;

internal static class CompatibilitySmoke
{
    public static async Task Run(string[] targets)
    {
        if (targets.Length == 0) targets = ["mysql57", "mysql84", "postgres16", "postgres17", "postgres18"];
        foreach (var target in targets) await Check(target);
    }

    private static async Task Check(string target)
    {
        var (image, type, maria) = target switch
        {
            "mysql57" => ("mysql:5.7", DatabaseType.MySql, false),
            "mysql84" => ("mysql:8.4", DatabaseType.MySql, false),
            "mariadb1011" => ("mariadb:10.11", DatabaseType.MySql, true),
            "postgres16" => ("postgres:16-bookworm", DatabaseType.PostgreSql, false),
            "postgres17" => ("postgres:17", DatabaseType.PostgreSql, false),
            "postgres18" => ("postgres:18-alpine", DatabaseType.PostgreSql, false),
            _ => throw new ArgumentException("Supported fixtures: mysql57, mysql84, mariadb1011, postgres16, postgres17, postgres18.")
        };
        var source = "octobackup-compat-" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(Path.GetTempPath(), source + ".zip");
        var rawPath = Path.ChangeExtension(path, ".sql");
        var env = new Dictionary<string, string?>();
        var password = "Ob!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        env["POSTGRES_PASSWORD"] = env["PGPASSWORD"] = env["MYSQL_ROOT_PASSWORD"] = env["MYSQL_PWD"] = password;
        var commands = new DockerRestoreContainerCommands();
        var runner = new DockerRestoreTestRunner(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:Enabled"] = "true" }).Build(), commands);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var token = deadline.Token;
        var test = new BackupTest { Id = Guid.NewGuid(), DatabaseType = type, DatabaseName = "compat_data" };
        try
        {
            var run = new List<string> { "run", "--detach", "--pull=never", "--name", source, "--network=none", "--memory=2g", "--pids-limit=256", "--log-driver=none" };
            run.AddRange(type == DatabaseType.PostgreSql
                ? ["-e", "POSTGRES_PASSWORD", "-e", "POSTGRES_DB=compat_data", "-e", "PGDATA=/var/lib/postgresql/restore-data", "--tmpfs", "/var/lib/postgresql:rw,size=1g", "--tmpfs", "/var/lib/postgresql/data:rw,size=1m"]
                : new[] { "-e", "MYSQL_ROOT_PASSWORD", "-e", "MYSQL_DATABASE=compat_data", "--tmpfs", "/var/lib/mysql:rw,size=1g" });
            run.Add(image);
            await commands.Run(run, env, null, token);
            List<string> Client(string sql)
            {
                var args = new List<string> { "exec", "-e", type == DatabaseType.PostgreSql ? "PGPASSWORD" : "MYSQL_PWD", source };
                args.AddRange(type == DatabaseType.PostgreSql
                    ? ["psql", "-h", "127.0.0.1", "-U", "postgres", "-d", "compat_data", "-v", "ON_ERROR_STOP=1", "-Atqc", sql]
                    : new[] { maria ? "mariadb" : "mysql", "--host=127.0.0.1", "--protocol=TCP", "--user=root", "--default-character-set=utf8mb4", "--database=compat_data", "--execute", sql });
                return args;
            }
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(120));
                while (true)
                {
                    try { await commands.Run(Client("SELECT 1"), env, null, startup.Token); break; }
                    catch (RestoreTestException) { await Task.Delay(1000, startup.Token); }
                }
            }
            var seed = type == DatabaseType.PostgreSql
                ? "CREATE TABLE probe(id int PRIMARY KEY,value text,payload bytea,doc jsonb,optional text); INSERT INTO probe VALUES(1,'verified-row',decode('00ff0100','hex'),'{\"valid\":true}',NULL); CREATE TABLE empty_probe(id int); CREATE SCHEMA reporting; CREATE TABLE reporting.unicode_probe(value text); INSERT INTO reporting.unicode_probe VALUES('Zażółć 🙂'); CREATE VIEW probe_view AS SELECT id,value FROM probe; CREATE FUNCTION probe_value() RETURNS text LANGUAGE SQL AS $$ SELECT value FROM probe LIMIT 1 $$;"
                : "CREATE TABLE probe(id int PRIMARY KEY,value varchar(100),payload varbinary(16),doc json,optional text,location point); INSERT INTO probe VALUES(1,'verified-row',0x00FF0100,'{\"valid\":true}',NULL,ST_GeomFromText('POINT(1 2)')); CREATE TABLE empty_probe(id int); CREATE TABLE unicode_probe(value varchar(100)) DEFAULT CHARSET=utf8mb4; INSERT INTO unicode_probe VALUES('Zażółć 🙂'); CREATE VIEW probe_view AS SELECT id,value FROM probe; CREATE PROCEDURE probe_value() SELECT value FROM probe LIMIT 1; CREATE TRIGGER probe_insert BEFORE INSERT ON probe FOR EACH ROW SET NEW.id=NEW.id;";
            if (maria) seed = seed.Replace("doc json", "doc longtext");
            await commands.Run(Client(seed), env, null, token);
            var dumpArgs = new List<string> { "exec", "-e", type == DatabaseType.PostgreSql ? "PGPASSWORD" : "MYSQL_PWD", source };
            dumpArgs.AddRange(type == DatabaseType.PostgreSql
                ? ["pg_dump", "-h", "127.0.0.1", "-U", "postgres", "--no-owner", "--no-privileges", "-d", "compat_data"]
                : new[] { maria ? "mariadb-dump" : "mysqldump", "--host=127.0.0.1", "--protocol=TCP", "--user=root", "--default-character-set=utf8mb4", "--routines", "--events", "--triggers", "--single-transaction", "--databases", "compat_data" });
            if (type == DatabaseType.MySql && !maria) dumpArgs.InsertRange(dumpArgs.Count - 2, ["--no-tablespaces", "--set-gtid-purged=OFF"]);
            // Use the application's byte-preserving exporter, never decode a dump
            // as text: mysqldump can include non-UTF8 binary/geometry literals.
            await DumpProcess.Run("docker", dumpArgs, env, rawPath, cancellationToken: token);
            WriteArchive(path, rawPath, corrupt: false);
            var preview = await runner.Run(test, path, token);
            if (preview.TableCount != 3 || !preview.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Contains("verified-row") ||
                !preview.Tables.Any(t => t.Name == "empty_probe" && t.Rows.Count == 0) ||
                !preview.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Contains("Zażółć 🙂"))
                throw new InvalidOperationException("Real export/restore preview did not preserve expected fixture data and table inventory.");
            if (type == DatabaseType.MySql && !preview.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Contains("0x00FF0100"))
                throw new InvalidOperationException("Binary preview did not preserve fixture bytes.");
            Console.WriteLine($"{target}: real client dump restored; 3 user tables, empty table, Unicode, NULL, binary/JSON/spatial values and routines/views exported; samples matched.");
            await CleanupTest();
            if (type == DatabaseType.MySql)
            {
                test = test with { Id = Guid.NewGuid() };
                File.Delete(rawPath);
                dumpArgs.Insert(dumpArgs.Count - 2, "--hex-blob");
                await DumpProcess.Run("docker", dumpArgs, env, rawPath, cancellationToken: token);
                WriteArchive(path, rawPath, corrupt: false);
                var hexPreview = await runner.Run(test, path, token);
                if (!hexPreview.Tables.SelectMany(t => t.Rows).SelectMany(r => r).Contains("0x00FF0100"))
                    throw new InvalidOperationException("Hex-encoded export did not preserve binary fixture bytes.");
                Console.WriteLine($"{target}: --hex-blob export restored with matching binary sample.");
                await CleanupTest();
            }
            test = test with { Id = Guid.NewGuid() };
            WriteArchive(path, rawPath, corrupt: true);
            var rejected = false;
            try { await runner.Run(test, path, token); }
            catch (RestoreTestException error) when (error.Message.StartsWith("Restoring backup archive:", StringComparison.Ordinal)) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Corrupt real dump was not rejected during restore.");
            Console.WriteLine($"{target}: corrupt real dump rejected at restore stage.");
        }
        finally
        {
            await CleanupTest();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await commands.Run(["rm", "--force", "--volumes", source], env, null, cleanup.Token);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(rawPath)) File.Delete(rawPath);
        }
        async Task CleanupTest()
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runner.Cleanup(test.Id, cleanup.Token);
        }
    }

    private static void WriteArchive(string path, string sqlPath, bool corrupt)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var output = archive.CreateEntry("actual-dump.sql").Open();
        using var input = File.OpenRead(sqlPath);
        if (corrupt) output.Write(System.Text.Encoding.UTF8.GetBytes("THIS IS NOT VALID SQL;\n"));
        input.CopyTo(output);
        // Force the client to exit while there is still data to stream. This
        // exercises broken-pipe handling without losing the provider failure.
        if (corrupt)
        {
            var padding = System.Text.Encoding.UTF8.GetBytes("\n-- " + new string('x', 1024));
            for (var i = 0; i < 8192; i++) output.Write(padding);
        }
    }
}
