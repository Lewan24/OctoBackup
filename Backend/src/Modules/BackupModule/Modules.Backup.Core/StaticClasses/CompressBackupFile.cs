using System.IO.Compression;

namespace Modules.Backup.Core.StaticClasses;

public static class CompressBackupFile
{
    /// <summary>
    ///     Performs zip compression on specific file. Method creates zip file with current short date
    /// </summary>
    /// <example>
    ///     File name: 11.08.2023.sql //
    ///     Zip file: 11.08.2023.zip //
    ///     File inside zip: 11.08.2023.sql
    /// </example>
    /// <param name="fileBasePath">Base path of directory where the file currently is</param>
    /// <param name="fileName">name of file that we want to compress</param>
    /// <param name="fileExtension">extension of created file like .sql or .bak</param>
    /// <returns>string of zip file name that contains full path</returns>
    public static string Perform(string fileBasePath, string fileName, string fileExtension = ".sql")
    {
        try
        {
            var completeFilePath = Path.Combine(fileBasePath, fileName);
            var zipFileName = Path.Combine(fileBasePath, $"{fileName.Split(fileExtension)[0]}.zip");

            if (File.Exists(zipFileName))
                throw new Exception("Zip with the same name already exists. Stopping compression.");

            using (var sourceStream = new FileStream(completeFilePath, FileMode.Open))
            {
                using var zipStream = new FileStream(zipFileName, FileMode.CreateNew);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
                var entryName = Path.GetFileName(completeFilePath);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using var entryStream = entry.Open();
                sourceStream.CopyTo(entryStream);
            }

            if (File.Exists(completeFilePath))
                File.Delete(completeFilePath);

            return zipFileName;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
