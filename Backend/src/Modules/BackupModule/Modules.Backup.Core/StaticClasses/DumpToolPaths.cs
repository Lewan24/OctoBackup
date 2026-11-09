namespace Modules.Backup.Core.StaticClasses;

// Executables are deployment configuration, never connection/API input.
public static class DumpToolPaths
{
    public static string Configured(string value, string setting)
    {
        if (!Path.IsPathFullyQualified(value) || !File.Exists(value))
            throw new DumpFailureException("dump client", null, $"{setting} must point to an installed executable using an absolute path. Configure it on the API host, not the database host.");
        return value;
    }

    public static IEnumerable<string> MySqlCandidates(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) { yield return Configured(configured, "DatabaseTools:MySqlDumpPath"); yield break; }
        yield return "mysqldump"; yield return "mariadb-dump";
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var root in ProgramDirectories())
        {
            var mysql = Path.Combine(root, "MySQL");
            if (Directory.Exists(mysql))
                foreach (var installation in Directory.EnumerateDirectories(mysql).OrderDescending())
                    foreach (var relative in new[] { "bin/mysqldump.exe", "mysqldump.exe" })
                    {
                        var path = Path.Combine(installation, relative);
                        if (File.Exists(path)) yield return path;
                    }
            foreach (var installation in Directory.EnumerateDirectories(root, "MariaDB*"))
            {
                var path = Path.Combine(installation, "bin", "mariadb-dump.exe");
                if (File.Exists(path)) yield return path;
            }
        }
    }

    public static string Postgres(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Configured(configured, "DatabaseTools:PostgresDumpPath");
        if (OperatingSystem.IsWindows())
            foreach (var root in ProgramDirectories())
                for (var major = 18; major >= 14; major--)
                {
                    var path = Path.Combine(root, "PostgreSQL", major.ToString(), "bin", "pg_dump.exe");
                    if (File.Exists(path)) return path;
                }
        return "pg_dump";
    }

    private static IEnumerable<string> ProgramDirectories() => new[] {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
}
