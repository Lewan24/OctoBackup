using Modules.Shared.Common;

namespace Modules.Backup.Core.StaticClasses;

public static class SafeBackupPath
{
    public static string Root => DbCommon.BackupDirectory;

    public static bool IsWithinRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        // Storage must not follow symlinks into unrelated files.
        for (var current = new FileInfo(full) as FileSystemInfo; current is not null && current.FullName.Length >= root.Length - 1;
             current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        return true;
    }

    public static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(['/', '\\', ':']) >= 0 ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid backup path segment.");
        return value;
    }
}
