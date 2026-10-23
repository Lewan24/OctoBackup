using Modules.Shared.Common;

namespace Modules.Backup.Core.StaticClasses;

public sealed record SqlServerBackupStorage(string LocalPath, string RemotePath, bool CopyRequired)
{
    public static SqlServerBackupStorage Resolve(string output, string? remoteRoot, string? stagingDirectory)
    {
        if (!SafeBackupPath.IsWithinRoot(output)) throw Failure("The local backup destination is invalid.");
        if (!string.IsNullOrWhiteSpace(stagingDirectory))
        {
            if (string.IsNullOrWhiteSpace(remoteRoot) || !Path.IsPathFullyQualified(stagingDirectory) || !Directory.Exists(stagingDirectory))
                throw Failure("SQL Server staging requires an existing SqlServer:LocalStagingDirectory mounted in the API and SqlServer:RemoteBackupRoot pointing to the same shared directory on SQL Server.");
            var local = Path.Combine(Path.GetFullPath(stagingDirectory), SafeBackupPath.Segment(Path.GetFileName(output)));
            CheckLinks(local);
            return new(local, Remote(remoteRoot, Path.GetFileName(output)), true);
        }
        // Compatibility for existing installations sharing the complete backup root.
        var relative = Path.GetRelativePath(SafeBackupPath.Root, output);
        return new(output, string.IsNullOrWhiteSpace(remoteRoot) ? output : Remote(remoteRoot, relative), false);
    }

    private static string Remote(string root, string relative)
    {
        if (!(root.StartsWith('/') || root.StartsWith(@"\\") || System.Text.RegularExpressions.Regex.IsMatch(root, @"\A[A-Za-z]:[\\/]")) || root.Contains('\0'))
            throw Failure("SqlServer:RemoteBackupRoot must be an absolute path on the SQL Server host.");
        var separator = root.Contains('\\') ? '\\' : '/';
        return root.TrimEnd('/', '\\') + separator + relative.Replace(Path.DirectorySeparatorChar, separator);
    }

    public static void CheckLinks(string path)
    {
        for (FileSystemInfo? item = new FileInfo(path); item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if (item.LinkTarget is not null || (item.Exists && item.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw Failure("SQL Server staging must not contain symbolic links.");
    }

    public static DumpFailureException Failure(string hint) => new("SQL Server", null, hint);
}
