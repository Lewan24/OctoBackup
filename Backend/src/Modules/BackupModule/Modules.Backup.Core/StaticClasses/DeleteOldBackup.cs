using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Modules.Backup.Core.StaticClasses;

public static class DeleteOldBackup
{
    public static Task Delete(string directoryPath, int daysThreshold, ILogger logger)
    {
        try
        {
            var zipFiles = Directory.GetFiles(directoryPath, "*.zip");
            var deleted = 0;

            foreach (var zipFile in zipFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(zipFile);
                var match = Regex.Match(fileName, @"\d{2,4}\.\d{1,2}\.\d{1,2}\.\d{1,2}\.\d{1,2}");

                if (!match.Success)
                    continue;

                var backupDateTime = DateTime.ParseExact(match.Value, "yyyy.MM.dd.HH.mm", CultureInfo.InvariantCulture);

                var timeDifference = DateTime.Now - backupDateTime;

                if (timeDifference.TotalDays < daysThreshold)
                    continue;

                File.Delete(zipFile);
                deleted++;
            }

            logger.LogInformation("Successfully deleted [{DeletedFilesCount}] old backups.", deleted);
            return Task.CompletedTask;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to delete old backups [{ErrorMsg}]", e.Message);
            return Task.CompletedTask;
        }
    }
}
