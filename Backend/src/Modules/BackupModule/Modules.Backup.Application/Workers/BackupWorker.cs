using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modules.Backup.Application.Services;
using Modules.Shared.Settings;

namespace Modules.Backup.Application.Workers;

public sealed class BackupWorker(IServiceScopeFactory scopes, ILogger<BackupWorker> logger, IApplicationSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BackupQueueProcessor>().RecoverInterrupted(stoppingToken);
        await Task.WhenAll(Consume(stoppingToken), Schedule(stoppingToken));
    }

    private async Task Consume(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BackupQueueProcessor>().ProcessNext(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Backup queue processing failed; durable requests remain available"); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }

    private async Task Schedule(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<BackupQueueService>().EnqueueDueSchedules(token);
                if (count > 0) logger.LogInformation("Enqueued {Count} scheduled backup requests", count);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Could not enqueue due schedules"); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.Current.SchedulePollSeconds), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
}
