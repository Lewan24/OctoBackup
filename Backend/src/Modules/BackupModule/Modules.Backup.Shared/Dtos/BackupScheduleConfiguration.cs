namespace Modules.Backup.Shared.Dtos;

public record BackupScheduleConfiguration
{
    /// <summary>
    ///     Lista dni w tygodniu, w których backup ma być uruchamiany.
    /// </summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary>
    ///     Konkretne godziny w danym dniu (np. 2:00, 14:00, 23:30).
    /// </summary>
    public List<TimeOnly> Times { get; set; } = new();

    /// <summary>
    ///     (Opcjonalnie) – czy harmonogram jest cykliczny co X minut/godzin zamiast stałych godzin.
    /// </summary>
    public TimeSpan? Interval { get; set; }
}
