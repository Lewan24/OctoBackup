using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;

namespace Modules.Backup.Application.Services;

// Header text is untrusted. Read a bounded prefix, recognize fixed dump markers,
// and select only deployment-owned image mappings; never construct an image from SQL.
public sealed record RestoreArchiveProfile(string Image, string Database, bool MariaDb, int? PostgresMajor, string Description)
{
    public string? MySqlRelease { get; init; }
    public static async Task<RestoreArchiveProfile> Read(BackupTest test, string path, IConfiguration config, CancellationToken token)
    {
        if (!Enum.IsDefined(test.DatabaseType)) throw new RestoreTestException("Unsupported database type.");
        if (test.DatabaseType == DatabaseType.SqlServer)
        {
            if (!Path.GetExtension(path).Equals(".bak", StringComparison.OrdinalIgnoreCase))
                throw new RestoreTestException("SQL Server verification requires a native .bak artifact.");
            return new(config["RestoreTests:SqlServerImage"] ?? "mcr.microsoft.com/mssql/server:2022-latest", "restore_probe", false, null, "SQL Server native backup");
        }
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();
        var maximum = Math.Clamp(config.GetValue("RestoreTests:MaxArchiveGiB", 10), 1, 100) * 1024L * 1024 * 1024;
        if (entries.Length != 1 || !entries[0].Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || entries[0].Length > maximum)
            throw new RestoreTestException("Expected one SQL file within the configured archive size limit.");
        await using var entry = entries[0].Open();
        var buffer = new byte[65536];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await entry.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0) break;
            length += read;
        }
        return FromHeader(test, Encoding.UTF8.GetString(buffer, 0, length), config);
    }

    public static RestoreArchiveProfile FromHeader(BackupTest test, string header, IConfiguration config)
    {
        var pg = header.Contains("-- PostgreSQL database dump", StringComparison.Ordinal);
        var mysql = header.Contains("-- MySQL dump", StringComparison.Ordinal) || header.Contains("-- MariaDB dump", StringComparison.Ordinal);
        if ((pg && test.DatabaseType != DatabaseType.PostgreSql) || (mysql && test.DatabaseType != DatabaseType.MySql))
            throw new RestoreTestException("The SQL archive format does not match the backup's recorded database engine. Check connection metadata and create a new backup.");
        if (test.DatabaseType == DatabaseType.PostgreSql)
        {
            var versions = Regex.Matches(header, @"(?m)^-- Dumped (?:from database|by pg_dump) version ([0-9]{1,2})(?:\.[0-9]+)*[^\r\n]*")
                .Select(m => int.Parse(m.Groups[1].Value)).ToArray();
            int? major = versions.Length == 0 ? null : versions.Max();
            var image = major is null ? config["RestoreTests:PostgresImage"] ?? "postgres:16-bookworm"
                : config[$"RestoreTests:PostgresImages:{major}"] ?? major switch {
                    14 => "postgres:14", 15 => "postgres:15", 16 => "postgres:16-bookworm", 17 => "postgres:17", 18 => "postgres:18-alpine",
                    _ => throw new RestoreTestException($"No compatible PostgreSQL image mapping exists for detected dump/source major {major}. Configure RestoreTests:PostgresImages:{major} and preload that image.") };
            return new(image, "restore_probe", false, major, major is null ? "PostgreSQL SQL archive (version header unavailable)" : $"PostgreSQL SQL archive, minimum compatible major {major}");
        }
        var server = Regex.Match(header, @"(?m)^-- Server version\s+(?:5\.5\.5-)?([0-9]{1,2}\.[0-9]{1,2})([^\r\n]*)");
        // Client branding is not the source engine: mariadb-dump can export MySQL.
        var maria = server.Success ? server.Value.Contains("MariaDB", StringComparison.OrdinalIgnoreCase)
            : header.Contains("-- MariaDB dump", StringComparison.Ordinal);
        var version = server.Success ? server.Groups[1].Value : null;
        var configured = version is null ? null : config[$"RestoreTests:{(maria ? "MariaDbImages" : "MySqlImages")}:{version}"];
        var mysqlImage = configured ?? (maria ? version switch {
            null => config["RestoreTests:MariaDbImage"] ?? "mariadb:11.4", "10.11" => "mariadb:10.11", "11.4" => "mariadb:11.4",
            _ => throw new RestoreTestException($"No MariaDB image mapping exists for detected source version {Regex.Match(server.Value, @"(?:5\.5\.5-)?[0-9]{1,2}\.[0-9]{1,2}(?:\.[0-9]{1,4})?").Value} (release {version}). Configure RestoreTests:MariaDbImages:{version} and preload a compatible image.")
        } : version switch {
            null => config["RestoreTests:MySqlImage"] ?? "mysql:8.4", "5.7" => "mysql:5.7", "8.0" => "mysql:8.0", "8.4" => "mysql:8.4",
            _ => throw new RestoreTestException($"No MySQL image mapping exists for detected source version {Regex.Match(server.Value, @"(?:5\.5\.5-)?[0-9]{1,2}\.[0-9]{1,2}(?:\.[0-9]{1,4})?").Value} (release {version}). Configure RestoreTests:MySqlImages:{version} and preload a compatible image.")
        });
        var databaseHeader = Regex.Match(header, @"(?m)^-- Current Database: `((?:``|[^`\r\n])+)`\r?$");
        var database = databaseHeader.Success ? databaseHeader.Groups[1].Value.Replace("``", "`")
            : string.IsNullOrWhiteSpace(test.DatabaseName) ? "restore_probe" : test.DatabaseName;
        if (database.Length > 64 || database.Any(char.IsControl) || new[] { "mysql", "sys", "information_schema", "performance_schema" }.Contains(database, StringComparer.OrdinalIgnoreCase))
            throw new RestoreTestException("The backup database name is invalid or is a protected system database.");
        return new(mysqlImage, database, maria, null, $"{(maria ? "MariaDB" : "MySQL")} SQL archive{(version is null ? " (version header unavailable)" : ", source " + version)}") { MySqlRelease = version };
    }
}
