using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Hubs;
namespace Modules.Backup.Application.Services;

public sealed class NotifyService(IHubContext<BackupHub> hub, BackupsDbContext db, AppIdentityDbContext users)
{
    public Task CallServerCreatedEvent(string userName) => ForUser(userName, "ServerCreatedEvent");
    public Task CallBackupCreatedEvent(string userName) => ForUser(userName, "BackupCreatedEvent");
    public Task CallServerHasChangedEvent(Guid id) => ForServer(id, "ServerHasChangedEvent");
    public Task CallQueueChangedEvent(Guid id) => ForServer(id, "BackupQueueChangedEvent");
    public Task CallScheduleCreatedEvent(Guid id) => ForServer(id, "ScheduleCreatedEvent");
    public async Task CallScheduleHasChangedEvent(Guid id)
    {
        var serverId = await db.Schedules.Where(s => s.Id == id).Select(s => s.DbConnectionId).FirstOrDefaultAsync();
        await ForServer(serverId, "ScheduleHasChangedEvent");
    }
    private async Task<List<string>> AdminIds() => await (from ur in users.UserRoles join r in users.Roles on ur.RoleId equals r.Id where r.Name == "Admin" select ur.UserId).ToListAsync();
    private async Task ForUser(string name, string evt)
    {
        var ids = await AdminIds();
        var id = await users.Users.Where(u => u.UserName == name).Select(u => u.Id).FirstOrDefaultAsync();
        if (id is not null) ids.Add(id);
        await hub.Clients.Users(ids.Distinct().ToList()).SendAsync(evt);
    }
    private async Task ForServer(Guid id, string evt)
    {
        var ids = await AdminIds();
        ids.AddRange((await db.UsersServers.Where(s => s.ServerId == id).Select(s => s.UserId).ToListAsync()).Select(x => x.ToString()));
        await hub.Clients.Users(ids.Distinct().ToList()).SendAsync(evt);
    }
}
