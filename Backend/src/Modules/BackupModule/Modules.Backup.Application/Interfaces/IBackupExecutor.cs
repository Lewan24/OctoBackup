using Modules.Backup.Core.Entities.DbContext;

namespace Modules.Backup.Application.Interfaces;

public sealed record BackupArtifact(Guid Id, string Name, string FilePath);

public interface IBackupExecutor
{
    Task<BackupArtifact> Execute(BackupRequest request, CancellationToken token);
}
