namespace Modules.Shared.Common;

public static class DbCommon
{
    public static string DataDirectory { get; private set; } = GetDbDirectory();

    public static string DbPath => Path.Join(DataDirectory, "Config", "OctoBackup.db");
    public static string BackupDirectory => Path.Combine(DataDirectory, "Backups");
    public static string? SshHostKeysFile { get; private set; } = Environment.GetEnvironmentVariable("OCTOBACKUP_SSH_HOST_KEYS");

    public static void Configure(string dataDirectory, string? sshHostKeysFile)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        SshHostKeysFile = sshHostKeysFile;
    }

    private static string GetDbDirectory()
        => Environment.GetEnvironmentVariable("OCTOBACKUP_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctoBackup");

    public static void CreateDbDirectoryIfNotExists()
    {
        if (!Directory.Exists(DataDirectory))
            Directory.CreateDirectory(DataDirectory);

        var configDir = Path.GetDirectoryName(DbPath) ?? DataDirectory;
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);
    }
}
