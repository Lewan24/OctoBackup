namespace Modules.Backup.Core.Entities.DbContext;

public sealed record UserNotificationsSettings
{
    public Guid Id { get; init; }
    public Guid UserId { get; set; }
    public bool IsEnabled { get; set; }

    public bool NotifyOnSuccessfulBackup { get; set; }
    public bool NotifyOnErrors { get; set; }
    public bool NotifyOnUpdates { get; set; }
    public bool NotifyOnChanges { get; set; }
    public bool NotifyOnFailedTests { get; set; }
    public bool NotifyOnFailedBackups { get; set; }
}
