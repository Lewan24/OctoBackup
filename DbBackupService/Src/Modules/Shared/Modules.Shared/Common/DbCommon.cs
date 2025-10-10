namespace Modules.Shared.Common;

public static class DbCommon
{
    private static readonly string DbFolder = GetDbDirectory();
    
    public static readonly string DbPath = Path.Join(DbFolder, "OctoBackup.db");

    private static string GetDbDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctoBackup");
    
    public static void CreateDbDirectoryIfNotExists()
    {
        if (!Directory.Exists(DbFolder))
            Directory.CreateDirectory(DbFolder);
    }
}