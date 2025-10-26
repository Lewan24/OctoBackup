using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modules.Backup.Api.Backups;
using Modules.Backup.Api.Schedules;
using Modules.Backup.Api.Servers;
using Modules.Backup.Api.Statistics;
using Modules.Backup.Application;
using Modules.Backup.Infrastructure;

namespace Modules.Backup.Api;

public static class Extensions
{
    public static IServiceCollection AddBackupModule(this IServiceCollection services)
    {
        services.AddInfrastructureLayer();
        services.AddApplicationLayer();

        return services;
    }

    public static WebApplication MapBackupModuleEndpoints(this WebApplication app)
    {
        app.MapBackupEndpoints();
        app.MapServersEndpoints();
        app.MapSchedulesEndpoints();
        app.MapStatsEndpoints();

        return app;
    }
}