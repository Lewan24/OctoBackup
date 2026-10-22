namespace Modules.Backup.Application.Services;

// This deployment supports one application replica per data directory. Database
// uniqueness still guards duplicate acceptance independently of these process gates.
public sealed class BackupQueueCoordinator : IDisposable
{
    public SemaphoreSlim Mutations { get; } = new(1, 1);
    public SemaphoreSlim Execution { get; } = new(1, 1);
    public void Dispose() { Mutations.Dispose(); Execution.Dispose(); }
}
