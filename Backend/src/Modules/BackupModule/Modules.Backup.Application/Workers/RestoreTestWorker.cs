using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Services;

namespace Modules.Backup.Application.Workers;

public sealed class RestoreTestWorker(IServiceScopeFactory scopes, ILogger<RestoreTestWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovered = false;
        var previousEnabled = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<RestoreTestService>();
                await service.SkipDisabledAutomaticTests(stoppingToken);
                var enabled = configuration.GetValue("RestoreTests:Enabled", false);
                if (!recovered || (enabled && !previousEnabled)) { await service.Recover(stoppingToken); recovered = true; }
                previousEnabled = enabled;
                await service.ProcessNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch
            {
                logger.LogWarning("Restore test worker unavailable; retrying durable work.");
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RestoreTestService>().MarkUnavailable(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { logger.LogWarning("Could not persist restore-runtime availability."); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
