using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Enums;
using OneOf;

namespace Modules.Backup.Application.Services;

public sealed class StatisticsService(BackupsDbContext appDb,
    AppIdentityDbContext identityDb,
    UserManager<AppUser> userManager,
    ServersService serversService)
{
    public async Task<OneOf<UserDashboardDto, string>> GetMyDashboard(string? identityName)
    {
        if (string.IsNullOrEmpty(identityName))
            return "Can't access identity name";

        var user = await userManager.FindByNameAsync(identityName);
        if (user is null)
            return "Can't find user";

        var myServersListResult = await serversService.GetAvailableServersBasic(identityName);
        var serversList = myServersListResult.IsT0 ? myServersListResult.AsT0 : new();
        
        var tunnelsCount = 0;
        var backupsCount = 0;
        
        var now = DateTime.UtcNow;
        var sixMonthsAgo = now.AddMonths(-6);
        List<PerformedBackup> backupsLast6Months  = new();
        
        var totalTests = 0;
        var successTests = 0;
        
        foreach (var server in serversList)
        {
            var dbServer = appDb.DbConnections.First(x => x.Id == server.Id);
            
            var tunnelId = dbServer?.TunnelId;
            var doesTunnelExist = appDb.DbServerTunnels.FirstOrDefault(x => x.Id == tunnelId);
            
            if (doesTunnelExist is not null)
                tunnelsCount++;

            var backups = appDb.Backups
                .AsNoTracking()
                .Where(x => x.ServerConnectionId == dbServer!.Id);
            
            backupsCount += backups.Count();
            backupsLast6Months.AddRange(backups
                .Where(b => b.CreatedOn >= sixMonthsAgo && b.ServerConnectionId == dbServer!.Id));

            foreach (var backup in backups)
            {
                if (backup.TestId is null)
                    continue;
                
                totalTests++;

                if (appDb.BackupsTests.First(x => x.Id == backup.TestId).Status == ETestStatus.Success)
                    successTests++;
            }
        }

        var avgBackupsPerMonth = backupsLast6Months
            .GroupBy(b => new { b.CreatedOn.Year, b.CreatedOn.Month })
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Average();
        
        var testsSuccessRate = totalTests == 0 ? 0 : (100 * successTests / totalTests);
        
        var dashboard = new UserDashboardDto
        {
            ServersCount = serversList.Count,
            TunnelsCount = tunnelsCount,
            PerformedBackups = backupsCount,
            AvgBackupsPerMonth = (int)avgBackupsPerMonth,
            PerformedTests = totalTests,
            TestsSuccessRate = testsSuccessRate
        };

        return dashboard;
    }

    public async Task<OneOf<AdminDashboardDto, string>> GetAdminDashboard()
    {
        var now = DateTime.UtcNow;
        var sixMonthsAgo = now.AddMonths(-6);

        var backupsLast6Months = await appDb.Backups
            .Where(b => b.CreatedOn >= sixMonthsAgo)
            .ToListAsync();

        var avgBackupsPerMonth = backupsLast6Months
            .GroupBy(b => new { b.CreatedOn.Year, b.CreatedOn.Month })
            .Select(g => g.Count())
            .DefaultIfEmpty(0)
            .Average();
        
        var totalTests = await appDb.BackupsTests.CountAsync();
        var successTests = await appDb.BackupsTests.CountAsync(x => x.Status == ETestStatus.Success);
        var testsSuccessRate = totalTests == 0 ? 0 : (100 * successTests / totalTests);
        
        var dashboard = new AdminDashboardDto
        {
            UsersCount = await identityDb.Users.CountAsync(),
            ServersCount = await appDb.DbConnections.CountAsync(),
            TunnelsCount = await appDb.DbServerTunnels.CountAsync(),
            AllBackups = await appDb.Backups.CountAsync(),
            AvgBackupsPerMonth = (int)avgBackupsPerMonth,
            AllTests = totalTests,
            TestsSuccessRate = testsSuccessRate
        };

        return dashboard;
    }
}