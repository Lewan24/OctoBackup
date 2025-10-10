namespace Modules.Backup.Shared.Dtos;

public sealed class AutoTestBackupConfigDto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool IsEnabled { get; set; } = true;
}