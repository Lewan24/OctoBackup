namespace Modules.Backup.Shared.Dtos;

public sealed record BackupRequestDto(Guid Id, Guid ServerId, string ServerName, string DatabaseName,
    string Source, string RequestedBy, string Status, DateTimeOffset CreatedAt, DateTimeOffset EligibleAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Message, Guid? BackupId, bool CanCancel, bool CanDelete);

public sealed record BackupQueueDto(IReadOnlyList<BackupRequestDto> Requests, int TotalCount,
    IReadOnlyList<Guid> ActiveServerIds,
    int QueuedCount, int RunningCount, bool WorkerBusy, BackupRequestDto? RunningRequest,
    BackupRequestDto? NextRequest, int CancellationDelaySeconds, int SpacingSeconds, DateTimeOffset ServerTimeUtc);
