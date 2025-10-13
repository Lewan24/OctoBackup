using Modules.Backup.Shared.Enums;

namespace Modules.Backup.Core.Entities.DbContext;

public sealed record DbServerConnection
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public bool IsDisabled { get; set; }
    public string ConnectionName { get; set; } = "Example";
    public required string ServerHost { get; set; }
    public required ushort ServerPort { get; set; }
    public required string DbName { get; set; }
    public DatabaseType DbType { get; set; } = DatabaseType.MySql;
    public required string DbUser { get; set; }
    public required string DbPasswd { get; set; }
    public bool IsTunnelRequired { get; set; }
    public Guid TunnelId { get; set; }
    public string? BackupEncryptionKeyHas { get; set; }
    public Guid AutoTestBackupsConfigId { get; set; }
}