using Modules.Backup.Shared.Enums;

namespace Modules.Backup.Core.Entities.DbContext;

public sealed record BackupTest
{
    public Guid Id { get; set; }
    public Guid BackupId { get; set; }
    public DateTime TestedOn { get; set; }
    public ETestStatus Status { get; set; } = ETestStatus.Running;
    public string? ErrorMessage { get; set; }
}