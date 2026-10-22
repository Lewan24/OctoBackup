namespace Modules.Backup.Core.Entities.Models;

public record ApplicationConfigurationModel
{
    public string? LogsFileName { get; init; } = string.Empty;
    public string? BackupSaveDirectory { get; init; } = string.Empty;
    public bool IncludeDateOfCreateLogFile { get; init; } = false;
    public int TimeInDaysToHoldBackups { get; init; } = 2;
}
