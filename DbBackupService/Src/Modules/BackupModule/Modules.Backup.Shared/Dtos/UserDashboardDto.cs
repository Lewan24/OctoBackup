namespace Modules.Backup.Shared.Dtos;

public class UserDashboardDto
{
    public int ServersCount { get; set; }
    public int TunnelsCount { get; set; }
    public int PerformedBackups { get; set; }
    public int AvgBackupsPerMonth { get; set; }
    public int PerformedTests { get; set; }
    public int TestsSuccessRate { get; set; }
}