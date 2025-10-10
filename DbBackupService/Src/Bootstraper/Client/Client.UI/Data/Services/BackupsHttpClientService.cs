using Modules.Backup.Shared.Dtos;
using OneOf;
using OneOf.Types;

namespace Client.UI.Data.Services;

public class BackupsHttpClientService(TokenHttpClientService api)
{
    public async Task<OneOf<List<ServerConnectionDto>, string>> GetServersAsync()
    {
        return await api.GetAsync<List<ServerConnectionDto>>("/api/servers/GetMyServers");
    }

    public async Task<OneOf<List<ServerNameIdDto>, string>> GetServersNamesForSchedulesAsync()
    {
        return await api.GetAsync<List<ServerNameIdDto>>("/api/servers/GetMyServersForSchedule");
    }

    public async Task<OneOf<Success, string>> CreateServerAsync(ServerConnectionDto newServer)
    {
        return await api.PostAsync("/api/servers/CreateServer", newServer);
    }

    public async Task<OneOf<Success, string>> EditServerAsync(ServerConnectionDto server)
    {
        return await api.PostAsync("/api/servers/EditServer", server);
    }

    public async Task<OneOf<Success, string>> ToggleServerDisabledStatus(Guid serverId)
    {
        return await api.PostAsync("/api/servers/ToggleServerDisabledStatus", serverId);
    }

    public async Task<OneOf<Success, string>> CascadeDeleteServer(Guid serverId)
    {
        return await api.PostAsync("/api/servers/CascadeDeleteServer", serverId);
    }

    public async Task<OneOf<Success, string>> TestServer(Guid serverId)
    {
        return await api.PostAsync("/api/servers/TestServerConnection", serverId);
    }

    public async Task<OneOf<List<BackupsScheduleDto>, string>> GetSchedulesAsync()
    {
        return await api.GetAsync<List<BackupsScheduleDto>>("/api/schedules/GetMySchedules");
    }

    public async Task<OneOf<Success, string>> CreateScheduleAsync(BackupsScheduleDto schedule)
    {
        return await api.PostAsync("/api/schedules/CreateSchedule", schedule);
    }

    public async Task<OneOf<Success, string>> EditScheduleAsync(BackupsScheduleDto schedule)
    {
        return await api.PostAsync("/api/schedules/EditSchedule", schedule);
    }

    public async Task<OneOf<Success, string>> DeleteScheduleAsync(Guid scheduleId)
    {
        return await api.PostAsync("/api/schedules/DeleteSchedule", scheduleId);
    }

    public async Task<OneOf<List<PerformedBackupDto>, string>> GetAllBackupsAsync()
    {
        return await api.GetAsync<List<PerformedBackupDto>>("/api/backups/GetAllBackups");
    }

    public async Task<OneOf<Success, string>> BackupServerAsync(Guid serverId)
    {
        return await api.PostAsync("/api/backups/PerformBackup", serverId);
    }
    
    public async Task<OneOf<AutoTestBackupConfigDto, string>> FetchAutoTestBackupConfig(Guid configurationId)
    {
        return await api.PostAsync<AutoTestBackupConfigDto, Guid>("/api/servers/FetchAutoTestConfig", configurationId);
    }
    
    public async Task<OneOf<Success, string>> EditAutoTestBackupConfig(AutoTestBackupConfigDto config)
    {
        return await api.PostAsync("/api/servers/EditAutoTestConfig", config);
    }
}