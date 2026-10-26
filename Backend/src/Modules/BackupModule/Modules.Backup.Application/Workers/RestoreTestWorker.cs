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
        string? previousFailure = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var retryDelay = TimeSpan.FromSeconds(3);
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<RestoreTestService>();
                await service.SkipDisabledAutomaticTests(stoppingToken);
                var enabled = configuration.GetValue("RestoreTests:Enabled", false);
                if (!recovered || (enabled && !previousEnabled)) { await service.Recover(stoppingToken); recovered = true; }
                previousEnabled = enabled;
                await service.ProcessNext(stoppingToken);
                if (previousFailure is not null)
                    logger.LogInformation("Restore test worker recovered; durable work can resume.");
                previousFailure = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // RestoreTestException messages are fixed safe diagnostics. Other
                // exception messages may include provider data, SQL or private paths.
                var reason = error is RestoreTestException safe ? safe.Message
                    : "Restore work could not access its metadata or runtime. Check storage permissions and deployment configuration.";
                retryDelay = TimeSpan.FromSeconds(30);
                if (reason != previousFailure)
                    logger.LogWarning("Restore test worker unavailable: {Reason} Durable work will retry every 30 seconds. Use the restore-runner image and configured daemon access, or disable RestoreTests:Enabled.", reason);
                previousFailure = reason;
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RestoreTestService>().MarkUnavailable(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { logger.LogWarning("Could not persist restore-runtime availability."); }
            }
            try { await Task.Delay(retryDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
