using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration.Json;

namespace Server.Api.Configuration;

public static class DevelopmentSettings
{
    public const string FileName = "appsettings.Local.json";

    public static bool Configure(ConfigurationManager configuration, string contentRoot, bool isDevelopment)
    {
        if (!isDevelopment) return false;
        var localFile = Path.Combine(contentRoot, FileName);
        var created = false;
        if (!File.Exists(localFile) &&
            configuration.GetValue<bool>("Development:AutoProvisionLocalSettings") &&
            string.IsNullOrWhiteSpace(configuration["CRYPTO_KEY"]))
        {
            var existing = ReadRepositoryEnvironment(contentRoot);
            var dataDirectory = ResolveDataDirectory(configuration, contentRoot);
            if (string.IsNullOrWhiteSpace(existing.GetValueOrDefault("CRYPTO_KEY")) &&
                File.Exists(Path.Combine(dataDirectory, "Config", "OctoBackup.db")))
                throw new InvalidOperationException("An existing OctoBackup database was found but its encryption key is missing. Restore CRYPTO_KEY in appsettings.Local.json, user-secrets or the environment; a replacement key will not decrypt existing credentials.");

            var settings = new Dictionary<string, object?>
            {
                ["CRYPTO_KEY"] = existing.GetValueOrDefault("CRYPTO_KEY") ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Bootstrap"] = new
                {
                    AdminEmail = existing.GetValueOrDefault("ADMIN_EMAIL") ?? "admin@localhost.example",
                    AdminPassword = existing.GetValueOrDefault("ADMIN_PASSWORD") ?? "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24))
                }
            };
            // Never replace a key created by another starting process.
            try
            {
                using var stream = new FileStream(localFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(localFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                created = true;
            }
            catch (IOException) when (File.Exists(localFile)) { }
        }

        // Local JSON overrides checked-in appsettings, while user-secrets, environment
        // variables and command-line configuration retain their normal higher priority.
        var sources = configuration.Sources;
        var insertAt = 0;
        for (var index = 0; index < sources.Count; index++)
            if (sources[index] is JsonConfigurationSource source &&
                Path.GetFileName(source.Path) is "appsettings.json" or "appsettings.Development.json")
                insertAt = index + 1;
        var localSource = new JsonConfigurationSource { Path = localFile, Optional = true, ReloadOnChange = false };
        localSource.ResolveFileProvider();
        sources.Insert(insertAt, localSource);
        return created;
    }

    public static string ResolveDataDirectory(IConfiguration configuration, string contentRoot)
    {
        var configured = configuration["OCTOBACKUP_DATA"];
        if (string.IsNullOrWhiteSpace(configured)) configured = configuration["Storage:DataDirectory"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctoBackup")
            : Path.GetFullPath(configured, contentRoot);
    }

    private static Dictionary<string, string> ReadRepositoryEnvironment(string contentRoot)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "OctoBackup.sln"))) continue;
            var file = Path.Combine(directory.FullName, ".env");
            if (!File.Exists(file)) break;
            foreach (var line in File.ReadLines(file))
            {
                var separator = line.IndexOf('=');
                if (separator < 1) continue;
                var name = line[..separator].Trim();
                if (name is not ("CRYPTO_KEY" or "ADMIN_EMAIL" or "ADMIN_PASSWORD")) continue;
                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))) value = value[1..^1];
                if (!string.IsNullOrWhiteSpace(value)) result[name] = value;
            }
            break;
        }
        return result;
    }
}
