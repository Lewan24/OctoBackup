using Microsoft.Extensions.DependencyInjection;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Application.Services;

namespace Modules.Backup.Application;

public static class Extensions
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<ServersService>();
        services.AddScoped<SchedulesService>();
        services.AddScoped<IDbBackupService, DbBackupService>();
        services.AddScoped<StatisticsService>();

        services.AddTransient<NotifyService>();
        services.AddSingleton<BackupQueueCoordinator>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<BackupQueueService>();
        services.AddScoped<BackupQueueProcessor>();
        services.AddScoped<IBackupExecutor, BackupExecutor>();
        services.AddSingleton<RestoreTestCoordinator>();
        services.AddScoped<RestoreTestService>();
        services.AddScoped<IRestoreTestRunner, DockerRestoreTestRunner>();
        services.AddSingleton<IRestoreContainerCommands, DockerRestoreContainerCommands>();

        return services;
    }
}
