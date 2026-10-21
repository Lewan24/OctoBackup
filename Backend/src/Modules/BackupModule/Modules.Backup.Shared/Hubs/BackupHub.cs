using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace Modules.Backup.Shared.Hubs;
[Authorize]
public sealed class BackupHub : Hub { }
public static class BackupHubHelper
{
    public const string HubUrl = "/backuphub";
    public enum Events { ServerCreatedEvent, ServerHasChangedEvent, ScheduleCreatedEvent, ScheduleHasChangedEvent, BackupCreatedEvent }
}
