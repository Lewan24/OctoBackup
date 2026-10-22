using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Dtos;

namespace Modules.Backup.Application.Interfaces;

public interface IRestoreTestRunner
{
    Task<RestorePreview> Run(BackupTest test, string artifactPath, CancellationToken token);
    Task Cleanup(Guid testId, CancellationToken token);
    Task CleanupOrphans(CancellationToken token);
}
