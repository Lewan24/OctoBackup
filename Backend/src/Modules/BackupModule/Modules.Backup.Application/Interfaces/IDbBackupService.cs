using Modules.Backup.Shared.Dtos;
using OneOf;
using OneOf.Types;

namespace Modules.Backup.Application.Interfaces;

public interface IDbBackupService
{
    Task<OneOf<List<PerformedBackupDto>, string>> GetAllBackups(string? identityName);

    Task<OneOf<(string FilePath, string ContentType, string FileName), string>> DownloadBackup(Guid id,
        string? identityName);
}
