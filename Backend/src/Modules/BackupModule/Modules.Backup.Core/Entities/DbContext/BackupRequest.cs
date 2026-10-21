namespace Modules.Backup.Core.Entities.DbContext;

public sealed class BackupRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServerId { get; set; }
    public required string ServerName { get; set; }
    public required string DatabaseName { get; set; }
    public required string TargetKey { get; set; }
    public string? ActiveDatabaseKey { get; set; }
    public string? RequestedById { get; set; }
    public string? RequestedByName { get; set; }
    public string Source { get; set; } = "Manual";
    public string ScheduleIdsJson { get; set; } = "[]";
    public string Status { get; set; } = BackupRequestStatus.Queued;
    public long CreatedAt { get; set; }
    public long EligibleAt { get; set; }
    public long? StartedAt { get; set; }
    public long? FinishedAt { get; set; }
    public string? Message { get; set; }
    public Guid? BackupId { get; set; }
    public bool IsHidden { get; set; }
}

public static class BackupRequestStatus
{
    public const string Queued = "Queued", Running = "Running", Cancelling = "Cancelling",
        Succeeded = "Succeeded", Failed = "Failed", Cancelled = "Cancelled", Interrupted = "Interrupted";
    public static bool IsActive(string status) => status is Queued or Running or Cancelling;
}
