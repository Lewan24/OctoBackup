using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Enums;
using Modules.Shared.Common;

namespace Modules.Backup.Application.Services;

// The runtime endpoint and images are deployment-owned, never supplied by API callers.
// No host mounts, network, published ports, interactive SQL or source credentials.
public sealed class DockerRestoreTestRunner(IConfiguration config, IRestoreContainerCommands commands) : IRestoreTestRunner
{
    private string Instance => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(DbCommon.BackupDirectory))))[..16].ToLowerInvariant();
    private string Label => "octobackup.restore-instance=" + Instance;
    private string Name(Guid id) => $"octobackup-restore-{Instance}-{id:N}";
    private static string Literal(DatabaseType type, string value) => type == DatabaseType.MySql
        ? "CONVERT(0x" + Convert.ToHexString(Encoding.UTF8.GetBytes(value)) + " USING utf8mb4)"
        : (type == DatabaseType.SqlServer ? "N" : "") + "'" + value.Replace("'", "''") + "'";
    private static string Identifier(DatabaseType type, string value) => type switch
    {
        DatabaseType.MySql => "`" + value.Replace("`", "``") + "`",
        DatabaseType.PostgreSql => "\"" + value.Replace("\"", "\"\"") + "\"",
        _ => "[" + value.Replace("]", "]]") + "]"
    };

    public async Task<RestorePreview> Run(BackupTest test, string artifactPath, CancellationToken token)
    {
        var phase = "Checking archive compatibility";
        try
        {
            var profile = await RestoreArchiveProfile.Read(test, artifactPath, config, token);
            var type = test.DatabaseType;
            if (!Enum.IsDefined(type)) throw new RestoreTestException("Unsupported database type.");
            var password = "Ob!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var env = new Dictionary<string, string?> { ["POSTGRES_PASSWORD"] = password, ["MYSQL_ROOT_PASSWORD"] = password,
                ["MSSQL_SA_PASSWORD"] = password, ["PGPASSWORD"] = password, ["MYSQL_PWD"] = password, ["SQLCMDPASSWORD"] = password };
            var image = profile.Image;
            if (string.IsNullOrWhiteSpace(image) || image.StartsWith('-')) throw new RestoreTestException("Invalid deployment restore image.");
            var dataGiB = Math.Clamp(config.GetValue("RestoreTests:DataGiB", 4), 1, 64);
            var memoryMiB = Math.Clamp(config.GetValue("RestoreTests:MemoryMiB", 4096), 2048, 65536);
            var data = type switch { DatabaseType.MySql => "/var/lib/mysql", DatabaseType.PostgreSql => "/var/lib/postgresql", _ => "/var/opt/mssql" };
            var args = new List<string> { "run", "--detach", "--pull=never", "--name", Name(test.Id), "--label", Label,
                "--network=none", "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges:true",
                "--memory=" + memoryMiB + "m", "--memory-swap=" + memoryMiB + "m", "--cpus=2", "--pids-limit=256",
                "--log-driver=none", "--tmpfs", "/tmp:rw,nosuid,nodev,size=256m", "--tmpfs", "/run:rw,nosuid,nodev,size=16m",
                "--tmpfs", data + ":rw,nosuid,nodev,size=" + dataGiB + "g" + (type == DatabaseType.SqlServer ? ",uid=10001,gid=0" : "") };
            if (type != DatabaseType.SqlServer)
                args.AddRange(["--cap-add=CHOWN", "--cap-add=SETUID", "--cap-add=SETGID", "--cap-add=DAC_OVERRIDE", "--cap-add=FOWNER"]);
            // Cover the pre-18 image's declared VOLUME as well, without leaving an
            // anonymous host-backed volume. Explicit PGDATA works with 14–18 layouts.
            if (type == DatabaseType.PostgreSql)
                args.AddRange(["--tmpfs", "/var/lib/postgresql/data:rw,nosuid,nodev,size=1m", "-e", "PGDATA=/var/lib/postgresql/restore-data"]);
            if (profile.MariaDb)
                args.AddRange(["--tmpfs", "/run/mysqld:rw,nosuid,nodev,size=16m,mode=0755"]);
            args.AddRange(type switch {
                DatabaseType.MySql => new[] { "-e", "MYSQL_ROOT_PASSWORD", "-e", "MYSQL_DATABASE=" + profile.Database },
                DatabaseType.PostgreSql => new[] { "-e", "POSTGRES_PASSWORD", "-e", "POSTGRES_DB=restore_probe" },
                _ => new[] { "-e", "MSSQL_SA_PASSWORD", "-e", "ACCEPT_EULA=Y", "-e", "MSSQL_PID=Developer" } });
            args.Add(image);
            phase = "Creating test container";
            await Docker(args, env, null, token);

            // Bound readiness separately from the overall restore deadline.
            var ready = false;
            phase = "Waiting for the database to start";
            using (var startup = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                startup.CancelAfter(TimeSpan.FromSeconds(180));
                while (!ready)
                {
                    try { await Query(test, profile, "SELECT 1", env, startup.Token, master: true); ready = true; }
                    catch (RestoreTestException)
                    {
                        var running = await Docker(["inspect", "--format", "{{.State.Running}}", Name(test.Id)], env, null, startup.Token);
                        if (running.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
                            throw new RestoreTestException("The test database container exited before becoming ready. Check the configured image and startup resource requirements.");
                        await Task.Delay(1000, startup.Token);
                    }
                }
            }
            phase = "Checking database version";
            if (type == DatabaseType.PostgreSql && profile.PostgresMajor is int minimum)
            {
                var version = await Query(test, profile, "SELECT current_setting('server_version_num')", env, token);
                if (!int.TryParse(version.Trim(), out var number) || number / 10000 < minimum)
                    throw new RestoreTestException("The configured test image is older than the SQL dump requires. Select a compatible RestoreTests:PostgresImages mapping; SQL statements will not be dropped to hide incompatibility.");
            }
            if (type == DatabaseType.MySql && profile.MySqlRelease is not null)
            {
                var actual = await Query(test, profile, "SELECT VERSION()", env, token);
                var release = Regex.Match(actual.Trim(), @"\A(?:5\.5\.5-)?([0-9]{1,2}\.[0-9]{1,2})\.");
                if (!release.Success || release.Groups[1].Value != profile.MySqlRelease || actual.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) != profile.MariaDb)
                    throw new RestoreTestException("The test image does not match the dump's MySQL/MariaDB family and release. Correct the version-specific image mapping before retrying.");
            }
            phase = "Restoring backup archive";
            if (type == DatabaseType.SqlServer)
            {
                if (!Path.GetExtension(artifactPath).Equals(".bak", StringComparison.OrdinalIgnoreCase))
                    throw new RestoreTestException("SQL Server verification requires a native .bak artifact.");
                await Docker(["cp", artifactPath, Name(test.Id) + ":/var/opt/mssql/probe.bak"], env, null, token);
                await Docker(["exec", "--user=0", Name(test.Id), "chmod", "0644", "/var/opt/mssql/probe.bak"], env, null, token);
                var listing = await Query(test, profile, "RESTORE FILELISTONLY FROM DISK=N'/var/opt/mssql/probe.bak'", env, token, master: true);
                var moves = new List<string>();
                foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = line.Trim().Split('|');
                    if (fields.Length < 7 || fields[2].Trim() is not ("D" or "L"))
                        throw new RestoreTestException("The native backup contains an unsupported file layout.");
                    moves.Add("MOVE " + Literal(type, fields[0].Trim()) + " TO N'/var/opt/mssql/data/probe_" + moves.Count + (fields[2].Trim() == "L" ? ".ldf'" : ".mdf'"));
                }
                if (moves.Count == 0) throw new RestoreTestException("The native backup contains no restorable files.");
                await Query(test, profile, "RESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/probe.bak'; RESTORE DATABASE [restore_probe] FROM DISK=N'/var/opt/mssql/probe.bak' WITH " + string.Join(",", moves), env, token, master: true);
            }
            else
            {
                // Stream the only SQL entry; never extract archive paths onto the host.
                using var zip = ZipFile.OpenRead(artifactPath);
                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
                var maxBytes = Math.Clamp(config.GetValue("RestoreTests:MaxArchiveGiB", 10), 1, 100) * 1024L * 1024 * 1024;
                if (entries.Length != 1 || !entries[0].Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || entries[0].Length > maxBytes)
                    throw new RestoreTestException("Expected one SQL file within the configured archive size limit.");
                await using var sql = new BoundedArchiveStream(entries[0].Open(), maxBytes);
                await Docker(Client(test, profile, env, query: null), env, sql, token);
            }
            phase = "Inspecting restored tables and sample data";
            var preview = await Inspect(test, profile, env, token);
            return preview with { Notes = profile.Description + ". " + preview.Notes };
        }
        catch (RestoreTestException error) { throw new RestoreTestException(phase + ": " + error.Message); }
        catch (InvalidDataException) { throw new RestoreTestException(phase + ": The ZIP archive is invalid or damaged."); }
        catch (JsonException) { throw new RestoreTestException(phase + ": Database inspection returned invalid or truncated JSON data."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new RestoreTestException(phase + ": The database did not become ready within the startup deadline."); }
    }

    private List<string> Client(BackupTest test, RestoreArchiveProfile profile, Dictionary<string, string?> env, string? query, bool master = false)
    {
        var args = new List<string> { "exec", "-i" };
        var variable = test.DatabaseType switch { DatabaseType.MySql => "MYSQL_PWD", DatabaseType.PostgreSql => "PGPASSWORD", _ => "SQLCMDPASSWORD" };
        args.AddRange(["-e", variable, Name(test.Id)]);
        var database = master && test.DatabaseType == DatabaseType.SqlServer ? "master" : profile.Database;
        args.AddRange(test.DatabaseType switch {
            DatabaseType.MySql => new[] { profile.MariaDb ? "mariadb" : "mysql", "--protocol=TCP", "--host=127.0.0.1", "--user=root", "--batch", "--raw", "--skip-column-names", "--default-character-set=utf8mb4", "--database=" + database },
            DatabaseType.PostgreSql => new[] { "psql", "--host=127.0.0.1", "--username=postgres", "--no-password", "--no-psqlrc", "--quiet", "--tuples-only", "--no-align", "--set=ON_ERROR_STOP=1", "--dbname=" + database },
            _ => new[] { "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-C", "-b", "-r", "1", "-h", "-1", "-W", "-s", "|", "-w", "65535", "-y", "0", "-d", database } });
        if (profile.MariaDb) args.Add("--skip-ssl"); // Loopback inside a networkless sandbox.
        if (test.DatabaseType == DatabaseType.MySql) args.Add("--binary-mode");
        // sqlcmd -W and -y are incompatible; retain untruncated JSON output.
        if (test.DatabaseType == DatabaseType.SqlServer) args.Remove("-W");
        if (query is not null) args.AddRange([test.DatabaseType == DatabaseType.MySql ? "--execute" : test.DatabaseType == DatabaseType.PostgreSql ? "--command" : "-Q", query]);
        return args;
    }

    private Task<string> Query(BackupTest test, RestoreArchiveProfile profile, string sql, Dictionary<string, string?> env, CancellationToken token, bool master = false)
        => Docker(Client(test, profile, env, (test.DatabaseType == DatabaseType.SqlServer ? "SET NOCOUNT ON; " : test.DatabaseType == DatabaseType.PostgreSql ? "SET standard_conforming_strings=on; " : "") + sql, master), env, null, token);

    private async Task<RestorePreview> Inspect(BackupTest test, RestoreArchiveProfile profile, Dictionary<string, string?> env, CancellationToken token)
    {
        var type = test.DatabaseType;
        var catalog = type switch {
            DatabaseType.MySql => $"SELECT JSON_OBJECT('schema',table_schema,'name',table_name) FROM information_schema.tables WHERE table_schema={Literal(type, profile.Database)} AND table_type='BASE TABLE' LIMIT 1001",
            DatabaseType.PostgreSql => "SELECT json_build_object('schema',schemaname,'name',tablename) FROM pg_catalog.pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema') AND schemaname NOT LIKE 'pg_toast%' LIMIT 1001",
            _ => "SELECT TOP(1001) SCHEMA_NAME(schema_id) AS [schema],name FROM sys.tables WHERE is_ms_shipped=0 FOR JSON PATH" };
        var tables = Parse(await Query(test, profile, catalog, env, token), type).ToArray();
        if (tables.Length == 0) throw new RestoreTestException("Restore completed but no user tables were found.");
        if (tables.Length > 1000) throw new RestoreTestException("Restore exceeds the 1,000 user-table inspection limit.");
        var inventory = tables.Select(t => new RestoreTableName(t.GetProperty("schema").GetString()!, t.GetProperty("name").GetString()!))
            .OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal).ToArray();
        RandomNumberGenerator.Shuffle(tables.AsSpan());
        var samples = new List<RestoreTable>();
        foreach (var table in tables.Take(10))
        {
            var schema = table.GetProperty("schema").GetString()!;
            var name = table.GetProperty("name").GetString()!;
            var columnsSql = type switch {
                DatabaseType.MySql => $"SELECT JSON_OBJECT('name',column_name,'type',data_type) FROM information_schema.columns WHERE table_schema={Literal(type, schema)} AND table_name={Literal(type, name)} ORDER BY ordinal_position LIMIT 65",
                DatabaseType.PostgreSql => $"SELECT json_build_object('name',column_name) FROM information_schema.columns WHERE table_schema={Literal(type, schema)} AND table_name={Literal(type, name)} ORDER BY ordinal_position LIMIT 65",
                _ => $"SELECT TOP(65) c.name FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name={Literal(type, schema)} AND t.name={Literal(type, name)} ORDER BY c.column_id FOR JSON PATH" };
            var metadata = Parse(await Query(test, profile, columnsSql, env, token), type).ToArray();
            var columns = metadata.Select(c => c.GetProperty("name").GetString()!).ToArray();
            if (columns.Length > 64) columns = columns[..64];
            var qualified = Identifier(type, schema) + "." + Identifier(type, name);
            var expressions = columns.Select(c => type switch {
                DatabaseType.MySql => $"{Literal(type, c)},{MySqlCell(c, metadata)}",
                DatabaseType.PostgreSql => $"LEFT(CAST({Identifier(type, c)} AS text),256) AS {Identifier(type, c)}",
                _ => $"LEFT(CONVERT(nvarchar(max),{Identifier(type, c)}),256) AS {Identifier(type, c)}" });
            var rowsSql = type switch {
                DatabaseType.MySql => $"SELECT JSON_OBJECT({string.Join(",", expressions)}) FROM {qualified} LIMIT 20",
                DatabaseType.PostgreSql => $"SELECT row_to_json(probe) FROM (SELECT {string.Join(",", expressions)} FROM {qualified} LIMIT 20) probe",
                _ => $"SELECT TOP(20) {string.Join(",", expressions)} FROM {qualified} FOR JSON PATH, INCLUDE_NULL_VALUES" };
            var rows = Parse(await Query(test, profile, rowsSql, env, token), type).Select(row => (IReadOnlyList<string?>)columns.Select(c =>
                row.TryGetProperty(c, out var cell) && cell.ValueKind != JsonValueKind.Null ? cell.ToString() : null).ToArray()).ToArray();
            samples.Add(new RestoreTable(schema, name, columns, rows));
        }
        return new RestorePreview(tables.Length, samples,
            (samples.All(t => t.Rows.Count == 0) ? "Warning: no sampled table contained data. " : "Sampled rows were readable. ") +
            "Restore exited without errors. Up to 10 randomly selected user tables, 20 rows per table, 64 columns and 256 characters per cell are captured. Empty tables are reported, not treated as corruption. Samples do not prove source equality or business validity.") { Catalog = inventory };
    }

    private static string MySqlCell(string column, JsonElement[] metadata)
    {
        var field = Identifier(DatabaseType.MySql, column);
        var info = metadata.First(c => c.GetProperty("name").GetString() == column);
        var type = info.TryGetProperty("type", out var kind) ? kind.GetString() : null;
        return type switch
        {
            "binary" or "varbinary" or "tinyblob" or "blob" or "mediumblob" or "longblob" or "bit"
                => $"IF({field} IS NULL,NULL,CONCAT('0x',HEX(LEFT({field},127))))",
            "geometry" or "point" or "linestring" or "polygon" or "multipoint" or "multilinestring" or "multipolygon" or "geometrycollection"
                => $"LEFT(ST_AsText({field}),256)",
            _ => $"LEFT(CAST({field} AS CHAR CHARACTER SET utf8mb4),256)"
        };
    }

    private static IEnumerable<JsonElement> Parse(string output, DatabaseType type)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        if (type == DatabaseType.SqlServer)
        {
            using var document = JsonDocument.Parse(output.Replace("\r", "").Replace("\n", "").Trim());
            return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
        }
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => {
            using var document = JsonDocument.Parse(line.Trim()); return document.RootElement.Clone(); }).ToArray();
    }

    public async Task Cleanup(Guid testId, CancellationToken token)
    {
        if (!config.GetValue("RestoreTests:Enabled", false)) return;
        // rm is idempotent after confirming whether this exact generated name exists.
        var found = await Docker(["ps", "-aq", "--filter", "label=" + Label, "--filter", "name=^/" + Name(testId) + "$"], null, null, token);
        if (!string.IsNullOrWhiteSpace(found)) await Docker(["rm", "--force", "--volumes", Name(testId)], null, null, token);
    }

    public async Task CleanupOrphans(CancellationToken token)
    {
        if (!config.GetValue("RestoreTests:Enabled", false)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        var names = await Docker(["ps", "-a", "--filter", "label=" + Label, "--format", "{{.Names}}"], null, null, token);
        foreach (var name in names.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()))
        {
            var prefix = $"octobackup-restore-{Instance}-";
            if (name.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(name[prefix.Length..], "N", out _))
                await Docker(["rm", "--force", "--volumes", name], null, null, token);
        }
    }

    private Task<string> Docker(IEnumerable<string> arguments, Dictionary<string, string?>? env, Stream? input, CancellationToken token)
        => commands.Run(arguments, env, input, token);

    private sealed class BoundedArchiveStream(Stream inner, long maximum) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count); Check(read); return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken); Check(read); return read;
        }
        private void Check(int count)
        {
            _read += count;
            if (_read > maximum) throw new RestoreTestException("Decompressed archive exceeded the configured size limit.");
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public interface IRestoreContainerCommands
{
    Task<string> Run(IEnumerable<string> arguments, Dictionary<string, string?>? env, Stream? input, CancellationToken token);
}

public sealed class DockerRestoreContainerCommands : IRestoreContainerCommands
{
    public async Task<string> Run(IEnumerable<string> arguments, Dictionary<string, string?>? env, Stream? input, CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("docker") {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        var args = arguments.ToArray();
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (env is not null) foreach (var pair in env) process.StartInfo.Environment[pair.Key] = pair.Value;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(7200));
        Task<(string Text, bool Overflow)>? stdout = null, stderr = null;
        try
        {
            if (!process.Start()) throw new RestoreTestException("Container runtime could not start.");
            using var registration = deadline.Token.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
            stdout = Capture(process.StandardOutput, 4 * 1024 * 1024, deadline.Token);
            stderr = Capture(process.StandardError, 8192, deadline.Token);
            var inputFailed = false;
            try
            {
                if (input is not null) await input.CopyToAsync(process.StandardInput.BaseStream, deadline.Token);
                process.StandardInput.Close();
            }
            catch (IOException) { inputFailed = true; try { process.StandardInput.Close(); } catch { } }
            await process.WaitForExitAsync(deadline.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new RestoreTestException(FailureMessage(args.FirstOrDefault(), process.ExitCode, error.Text));
            if (inputFailed) throw new RestoreTestException("The complete SQL archive could not be streamed to the restore client. The test cannot be reported as successful.");
            if (output.Overflow) throw new RestoreTestException("Container response exceeded the inspection size limit.");
            return output.Text;
        }
        catch (System.ComponentModel.Win32Exception) { throw new RestoreTestException("Docker CLI is unavailable to the API service."); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            deadline.Cancel();
            // Observe both drains even when restore input fails or cancellation wins.
            try { if (stdout is not null) await stdout; } catch { }
            try { if (stderr is not null) await stderr; } catch { }
        }
    }

    public static string FailureMessage(string? operation, int exitCode, string error)
    {
        // Only fixed categories leave this boundary. Never include raw SQL,
        // database contents, credentials, image names or physical paths.
        var hint = error.Contains("No such image", StringComparison.OrdinalIgnoreCase) || error.Contains("Unable to find image", StringComparison.OrdinalIgnoreCase)
            ? "The configured database image is not preloaded. Pull the RestoreTests image on the test daemon before retrying."
            : error.Contains("permission denied", StringComparison.OrdinalIgnoreCase) || error.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            ? "The API service cannot access Docker or a required container resource. Check its runtime permissions."
            : error.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase) || error.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
            ? "The Docker engine is unavailable to the API service. Start the configured engine and check its endpoint."
            : error.Contains("read-only file system", StringComparison.OrdinalIgnoreCase)
            ? "The database image requires a writable path outside the configured temporary mounts. Check image compatibility."
            : error.Contains("unrecognized configuration parameter", StringComparison.OrdinalIgnoreCase) || error.Contains("unsupported version", StringComparison.OrdinalIgnoreCase)
            ? "The test database or client is older than the dump requires. Check the version-specific restore image mapping."
            : error.Contains("Unknown collation", StringComparison.OrdinalIgnoreCase) || error.Contains("Unknown character set", StringComparison.OrdinalIgnoreCase)
            ? "The dump requires a collation or character set unavailable in the selected engine image. Select a matching MySQL/MariaDB release."
            : error.Contains("syntax error", StringComparison.OrdinalIgnoreCase) || error.Contains("ERROR 1064", StringComparison.OrdinalIgnoreCase)
            ? "The SQL archive contains a syntax error or SQL incompatible with the selected engine/client. Restore stopped at the error."
            : error.Contains("does not exist", StringComparison.OrdinalIgnoreCase) && error.Contains("role", StringComparison.OrdinalIgnoreCase)
            ? "The SQL archive references a role missing from the disposable database. Create a new backup without source owners/ACLs."
            : "Check image compatibility, restore SQL and available container resources.";
        var step = operation switch { "run" => "Container creation", "exec" => "Database restore or inspection", "cp" => "Backup copy", "rm" => "Container cleanup", _ => "Docker runtime operation" };
        var code = Regex.Match(error, @"\bERROR ([0-9]{4})(?: \([A-Z0-9]{5}\))?(?: at line ([0-9]{1,10}))?\b");
        var location = code.Success ? $" Database error {code.Groups[1].Value}" + (code.Groups[2].Success ? $" at SQL line {code.Groups[2].Value}" : "") + "." : "";
        return $"{step} failed (exit {exitCode}).{location} {hint}";
    }

    private static async Task<(string Text, bool Overflow)> Capture(StreamReader reader, int limit, CancellationToken token)
    {
        var buffer = new char[4096]; var builder = new StringBuilder(); var overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var keep = Math.Min(count, limit - builder.Length);
            if (keep > 0) builder.Append(buffer, 0, keep);
            if (keep < count && limit > 0) overflow = true;
        }
        return (builder.ToString(), overflow);
    }
}
