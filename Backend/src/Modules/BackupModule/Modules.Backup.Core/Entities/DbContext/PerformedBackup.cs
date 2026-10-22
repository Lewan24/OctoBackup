namespace Modules.Backup.Core.Entities.DbContext;

public sealed record PerformedBackup
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedOn { get; set; }
    public Guid? TestId { get; set; }
    public Guid? ServerConnectionId { get; set; }
    public required string FilePath { get; set; }
    public bool IsSoftDeleted { get; set; } = false;
    public Modules.Backup.Shared.Enums.DatabaseType? DatabaseType { get; set; }
    public string? DatabaseName { get; set; }
}
