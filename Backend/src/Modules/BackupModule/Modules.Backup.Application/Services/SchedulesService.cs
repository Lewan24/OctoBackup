using Microsoft.EntityFrameworkCore;
using Modules.Administration.Shared.Interfaces;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Helpers;
using OneOf;
using OneOf.Types;

namespace Modules.Backup.Application.Services;

public sealed class SchedulesService(
    BackupsDbContext db,
    ServersService serversService,
    IAdminModuleApi adminApi,
    NotifyService notifyService)
{
    public async Task<OneOf<List<BackupsScheduleDto>, string>> GetMySchedules(string? identityName)
    {
        var getAvailableServersResult = await serversService.GetAvailableServersBasic(identityName);

        if (getAvailableServersResult.IsT1)
            return getAvailableServersResult.AsT1;

        var availableServers = getAvailableServersResult.AsT0 ?? new List<ServerNameIdDto>();

        var schedules = new List<BackupsScheduleDto>();

        var isAdmin = await adminApi.AmIAdmin(identityName);
        if (isAdmin)
        {
            schedules = db.Schedules.AsNoTracking().Select(x => new BackupsScheduleDto
            {
                Id = x.Id,
                Name = x.Name,
                ServerName = "",
                DbConnectionId = x.DbConnectionId,
                IsEnabled = x.IsEnabled,
                SelectedDays = x.Configuration!.Days,
                SelectedTimes = x.Configuration!.Times
            }).ToList();

            foreach (var schedule in schedules)
                schedule.ServerName = availableServers.FirstOrDefault(x => x.Id == schedule.DbConnectionId)?.Name ?? "Removed server";
        }
        else
        {
            foreach (var server in availableServers)
            {
                var tempDbSchedule = db.Schedules
                    .Where(x => x.DbConnectionId == server.Id)
                    .ToList();
                schedules.AddRange(tempDbSchedule.Select(x => new BackupsScheduleDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    ServerName = server.Name,
                    DbConnectionId = x.DbConnectionId,
                    IsEnabled = x.IsEnabled,
                    SelectedDays = x.Configuration!.Days,
                    SelectedTimes = x.Configuration!.Times
                }));
            }
        }

        return schedules;
    }

    public async Task<OneOf<Success, string>> CreateSchedule(BackupsScheduleDto schedule, string? identityName)
    {
        if (string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 120 || schedule.SelectedDays.Count is 0 or > 7 || schedule.SelectedDays.Any(d => !Enum.IsDefined(d)) || schedule.SelectedTimes.Count is 0 or > 24)
            return "Provide a name, valid days and up to 24 execution times.";
        var getAvailableServersResult = await serversService.GetAvailableServersBasic(identityName);
        if (getAvailableServersResult.IsT1)
            return getAvailableServersResult.AsT1;

        var availableServers = getAvailableServersResult.AsT0 ?? [];
        if (!await db.DbConnections.AnyAsync(s => s.Id == schedule.DbConnectionId && !s.IsDisabled)) return "Server is disabled or unavailable.";
        var isAdmin = await adminApi.AmIAdmin(identityName);

        if (!isAdmin && availableServers.All(s => s.Id != schedule.DbConnectionId))
            return "Can't access server";

        var entity = new BackupSchedule
        {
            Id = Guid.CreateVersion7(),
            Name = schedule.Name,
            IsEnabled = schedule.IsEnabled,
            DbConnectionId = schedule.DbConnectionId,
            Configuration = new BackupScheduleConfiguration
            {
                Days = schedule.SelectedDays,
                Times = schedule.SelectedTimes
            },
            NextBackupDate = BackupScheduleHelper.GetNextDateTime(schedule)
        };

        db.Schedules.Add(entity);
        await db.SaveChangesAsync();

        await notifyService.CallScheduleCreatedEvent(schedule.DbConnectionId);

        return new Success();
    }

    public async Task<OneOf<Success, string>> EditSchedule(BackupsScheduleDto schedule, string? identityName)
    {
        if (string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 120 || schedule.SelectedDays.Count is 0 or > 7 || schedule.SelectedDays.Any(d => !Enum.IsDefined(d)) || schedule.SelectedTimes.Count is 0 or > 24)
            return "Provide a name, valid days and up to 24 execution times.";
        var entity = await db.Schedules.FirstOrDefaultAsync(x => x.Id == schedule.Id);
        if (entity is null)
            return "Can't find schedule";

        var getAvailableServersResult = await serversService.GetAvailableServersBasic(identityName);
        if (getAvailableServersResult.IsT1)
            return getAvailableServersResult.AsT1;

        var availableServers = getAvailableServersResult.AsT0 ?? [];
        if (!await db.DbConnections.AnyAsync(s => s.Id == schedule.DbConnectionId && !s.IsDisabled)) return "Server is disabled or unavailable.";
        var isAdmin = await adminApi.AmIAdmin(identityName);

        if (!isAdmin && (availableServers.All(s => s.Id != entity.DbConnectionId) || availableServers.All(s => s.Id != schedule.DbConnectionId)))
            return "Can't access server";

        entity.Name = schedule.Name;
        entity.IsEnabled = schedule.IsEnabled;
        entity.DbConnectionId = schedule.DbConnectionId;
        entity.Configuration = new BackupScheduleConfiguration
        {
            Days = schedule.SelectedDays,
            Times = schedule.SelectedTimes
        };
        entity.NextBackupDate = BackupScheduleHelper.GetNextDateTime(schedule);

        db.Schedules.Update(entity);
        await db.SaveChangesAsync();

        await notifyService.CallScheduleHasChangedEvent(entity.Id);

        return new Success();
    }

    public async Task<OneOf<Success, string>> DeleteSchedule(Guid scheduleId, string? identityName)
    {
        var entity = await db.Schedules.FirstOrDefaultAsync(x => x.Id == scheduleId);
        if (entity is null)
            return "Schedule not found";

        var getAvailableServersResult = await serversService.GetAvailableServersBasic(identityName);
        if (getAvailableServersResult.IsT1)
            return getAvailableServersResult.AsT1;

        var availableServers = getAvailableServersResult.AsT0 ?? [];
        var isAdmin = await adminApi.AmIAdmin(identityName);

        if (!isAdmin && availableServers.All(s => s.Id != entity.DbConnectionId))
            return "Can't access server";

        db.Schedules.Remove(entity);
        await db.SaveChangesAsync();

        await notifyService.CallScheduleHasChangedEvent(scheduleId);

        return new Success();
    }
}
