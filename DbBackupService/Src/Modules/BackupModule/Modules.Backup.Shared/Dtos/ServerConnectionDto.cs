using System.ComponentModel.DataAnnotations;
using Modules.Backup.Shared.Enums;

namespace Modules.Backup.Shared.Dtos;

public record ServerConnectionDto
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    [Required] public string ConnectionName { get; set; } = "Example";
    [Required] public required string ServerHost { get; set; }
    [Required] public required short ServerPort { get; set; }
    [Required] public required string DbName { get; set; }
    [Required] public DatabaseType DbType { get; set; } = DatabaseType.MySql;
    [Required] public required string DbUser { get; set; }
    public string? DbPasswd { get; set; }
    public bool IsTunnelRequired { get; set; }

    public ServerTunnelDto? Tunnel { get; set; }
    public Guid AutoTestBackupsConfigId { get; set; }
}