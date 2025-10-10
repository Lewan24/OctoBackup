namespace Modules.Backup.Core.Entities.DbContext;

public sealed record AutomaticBackupTestConfig
{
    public Guid Id { get; init; }
    public Guid ServerId { get; set; }
    public bool IsEnabled { get; set; } = true;
}