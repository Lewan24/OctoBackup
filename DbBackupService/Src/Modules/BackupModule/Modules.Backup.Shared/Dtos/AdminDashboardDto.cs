namespace Modules.Backup.Shared.Dtos;

public class AdminDashboardDto
{
    public int UsersCount { get; set; }
    public int ServersCount { get; set; }
    public int TunnelsCount { get; set; }
    public int AllBackups { get; set; }
    public int AvgBackupsPerMonth { get; set; }
    public int AllTests { get; set; }
    public int TestsSuccessRate { get; set; }
}