using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Modules.Backup.Shared.Dtos;

namespace Modules.Backup.Core.Entities.DbContext;

public sealed record BackupSchedule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid DbConnectionId { get; set; }
    public string? ConfigurationJson { get; set; }

    [NotMapped]
    public BackupScheduleConfiguration? Configuration
    {
        get => string.IsNullOrWhiteSpace(ConfigurationJson)
            ? null
            : JsonSerializer.Deserialize<BackupScheduleConfiguration>(ConfigurationJson);

        set => ConfigurationJson = JsonSerializer.Serialize(value);
    }

    public DateTime NextBackupDate { get; set; }
}
