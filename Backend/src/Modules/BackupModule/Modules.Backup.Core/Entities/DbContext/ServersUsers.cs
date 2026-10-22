namespace Modules.Backup.Core.Entities.DbContext;

public sealed record ServersUsers
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid ServerId { get; set; }
}
