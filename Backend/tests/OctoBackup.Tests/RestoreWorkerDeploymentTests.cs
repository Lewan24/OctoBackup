using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Application.Services;
using Modules.Backup.Application.Workers;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Dtos;
using Xunit;

public sealed class RestoreWorkerDeploymentTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeFailure_ProvidesSafeGuidance_AndBackoffStopsPromptly(bool safeFailure)
    {
        using var factory = new QueueFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["RestoreTests:Enabled"] = "true" }));
            builder.ConfigureServices(services => {
                services.RemoveAll<IRestoreTestRunner>();
                services.AddSingleton<IRestoreTestRunner>(new UnavailableRunner(safeFailure));
            });
        });
        var logger = new WorkerLogger();
        using var worker = new RestoreTestWorker(host.Services.GetRequiredService<IServiceScopeFactory>(), logger,
            host.Services.GetRequiredService<IConfiguration>());
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var warning = await logger.Warning.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("30 seconds", warning);
            Assert.Contains("restore-runner", warning);
            Assert.Contains("RestoreTests:Enabled", warning);
            if (safeFailure) Assert.Contains("Docker CLI is unavailable", warning);
            Assert.DoesNotContain("private-provider-password", warning);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(timeout.Token);
        }
    }

    private sealed class UnavailableRunner(bool safeFailure) : IRestoreTestRunner
    {
        public Task CleanupOrphans(CancellationToken token) => throw (safeFailure
            ? new RestoreTestException("Docker CLI is unavailable to the API service.")
            : new InvalidOperationException("private-provider-password"));
        public Task Cleanup(Guid testId, CancellationToken token) => Task.CompletedTask;
        public Task<RestorePreview> Run(BackupTest test, string artifactPath, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class WorkerLogger : ILogger<RestoreTestWorker>
    {
        public TaskCompletionSource<string> Warning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (level == LogLevel.Warning) Warning.TrySetResult(format(state, error));
        }
    }
}
