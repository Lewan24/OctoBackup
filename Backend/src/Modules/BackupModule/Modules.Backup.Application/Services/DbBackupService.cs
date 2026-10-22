using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Auth.Core.Entities;
using Modules.Auth.Shared.Static.Entities;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.Databases;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.Interfaces;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Enums;
using Modules.Backup.Shared.Helpers;
using Modules.Crypto.Shared.Interfaces;
using OneOf;
using OneOf.Types;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Services;

internal sealed class DbBackupService(
    BackupsDbContext dbContext,
    UserManager<AppUser> userManager,
    ILogger<DbBackupService> logger)
    : IDbBackupService
{
    public async Task<OneOf<List<PerformedBackupDto>, string>> GetAllBackups(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
            return "Can't access user name";

        var user = await userManager.FindByNameAsync(identityName);
        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        List<DbServerConnection> dbServers;

        if (!isAdmin)
        {
            var userServers = dbContext.UsersServers
                .AsNoTracking()
                .Where(x => x.UserId == Guid.Parse(user.Id))
                .Select(x => x.ServerId)
                .ToList();

            if (userServers.Count == 0)
                return new List<PerformedBackupDto>();

            dbServers = dbContext.DbConnections
                .AsNoTracking()
                .Where(x => userServers.Contains(x.Id))
                .ToList();

            dbServers.RemoveAll(x => x.IsDisabled);
        }
        else
        {
            dbServers = dbContext.DbConnections
                .AsNoTracking()
                .ToList();
        }

        var backups = new List<PerformedBackupDto>();

        if (isAdmin)
            return dbContext.Backups
                .AsNoTracking()
                .Select(x => new PerformedBackupDto
                {
                    Id = x.Id,
                    CreatedOn = x.CreatedOn,
                    FilePath = null,
                    Name = x.Name,
                    ServerConnectionId = x.ServerConnectionId,
                    IsDisabled = x.IsSoftDeleted,
                    Test = dbContext.BackupsTests.AsNoTracking().Where(y => y.Id == x.TestId).Select(y =>
                        new BackupTestDto
                        {
                            Id = y.Id,
                            ErrorMessage = y.ErrorMessage,
                            Status = y.Status,
                            TestedOn = y.TestedOn
                        }).FirstOrDefault()
                }).ToList();

        foreach (var server in dbServers)
            backups.AddRange(dbContext.Backups
                .AsNoTracking()
                .Where(x => x.ServerConnectionId == server.Id && !x.IsSoftDeleted)
                .Select(x => new PerformedBackupDto
                {
                    Id = x.Id,
                    CreatedOn = x.CreatedOn,
                    FilePath = null,
                    Name = x.Name,
                    ServerConnectionId = x.ServerConnectionId,
                    Test = dbContext.BackupsTests.AsNoTracking().Where(y => y.Id == x.TestId).Select(y =>
                        new BackupTestDto
                        {
                            Id = y.Id,
                            ErrorMessage = y.ErrorMessage,
                            Status = y.Status,
                            TestedOn = y.TestedOn
                        }).FirstOrDefault()
                }));

        return backups;
    }

    public async Task<OneOf<(string FilePath, string ContentType, string FileName), string>> DownloadBackup(Guid id,
        string? identityName)
    {
        var backup = dbContext.Backups.AsNoTracking().FirstOrDefault(x => x.Id == id);
        if (backup is null)
            return "Can't find backup with specified id";

        if (string.IsNullOrWhiteSpace(identityName))
            return "You have to be logged in to have access to this api.";

        var user = await userManager.FindByNameAsync(identityName);
        if (user is null)
            return "Can't find user";

        var isAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);
        if (!isAdmin)
        {
            var hasAccess = dbContext.UsersServers.FirstOrDefault(x =>
                x.ServerId == backup.ServerConnectionId && x.UserId == Guid.Parse(user.Id));
            if (hasAccess is null)
                return "User don't have access to this server.";
        }

        if (backup.IsSoftDeleted) return "Backup has expired.";
        if (!SafeBackupPath.IsWithinRoot(backup.FilePath)) return "Backup file is outside the configured storage root.";
        if (!File.Exists(backup.FilePath))
            return "File does not exist.";

        var server = dbContext.DbConnections.AsNoTracking().FirstOrDefault(x => x.Id == backup.ServerConnectionId);

        var extension = Path.GetExtension(backup.FilePath).Equals(".bak", StringComparison.OrdinalIgnoreCase) ? ".bak" : ".zip";
        var fileName = server is not null
            ? $"{server.DbName}_{backup.CreatedOn:yyyy-MM-dd_HH-mm}{extension}"
            : $"DbBackup_{backup.CreatedOn:yyyy-MM-dd_HH-mm}{extension}";

        var filePath = backup.FilePath;
        if (!Path.IsPathRooted(filePath))
            filePath = Path.Combine(AppContext.BaseDirectory, filePath);

        logger.LogInformation("Downloading {BackupPath}...", filePath);
        return (filePath, extension == ".zip" ? "application/zip" : "application/octet-stream", fileName);
    }

}
