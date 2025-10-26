namespace Modules.Backup.Shared.Dtos;

public class PerformedBackupDto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string? Name { get; set; }
    public DateTime CreatedOn { get; set; }
    public BackupTestDto? Test { get; set; }
    public Guid? ServerConnectionId { get; set; }
    public string? FilePath { get; set; }
    public bool IsDisabled { get; set; } = false;
}