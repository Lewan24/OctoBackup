namespace Modules.Backup.Shared.Dtos;

public class BackupsScheduleDto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Guid DbConnectionId { get; set; }
    public string? ServerName { get; set; }

    public List<DayOfWeek> SelectedDays { get; set; } = new();
    public List<TimeOnly> SelectedTimes { get; set; } = new();
}
