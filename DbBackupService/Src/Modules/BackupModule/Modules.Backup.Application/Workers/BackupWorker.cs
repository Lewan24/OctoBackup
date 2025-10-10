using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modules.Backup.Application.Interfaces;

namespace Modules.Backup.Application.Workers;

public class BackupWorker(IServiceScopeFactory scopeFactory, ILogger<BackupWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        logger.LogInformation("Running backup worker in 20 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        logger.LogInformation("Worker will run each {Interval} minutes from now...", _interval.Minutes);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            try
            {
                using var scope = scopeFactory.CreateScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IDbBackupService>();

                var result = await backupService.BackupFromSchedules();

                result.Switch(
                    _ => logger.LogInformation("Work done."),
                    error => logger.LogWarning(error)
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while backing up schedules.");
            }
    }
}