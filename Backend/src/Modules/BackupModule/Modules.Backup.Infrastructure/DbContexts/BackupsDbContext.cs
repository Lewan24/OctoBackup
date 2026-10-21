using Microsoft.EntityFrameworkCore;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Shared.Common;

namespace Modules.Backup.Infrastructure.DbContexts;

public sealed class BackupsDbContext : DbContext
{
    public DbSet<DbServerConnection> DbConnections { get; set; }
    public DbSet<DbServerTunnel> DbServerTunnels { get; set; }
    public DbSet<BackupSchedule> Schedules { get; set; }
    public DbSet<PerformedBackup> Backups { get; set; }
    public DbSet<BackupTest> BackupsTests { get; set; }
    public DbSet<AutomaticBackupTestConfig> AutomaticBackupTestConfigs { get; set; }
    public DbSet<ServersUsers> UsersServers { get; set; }
    public DbSet<UserNotificationsSettings> UsersNotificationsSettings { get; set; }

    public DbSet<ServerBackupsConfiguration> Configurations { get; set; }
    public DbSet<BackupRequest> BackupRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var requests = modelBuilder.Entity<BackupRequest>();
        // SQLite permits multiple NULLs in a unique index; terminal requests release
        // their active key. This is the durable duplicate guard, including races.
        requests.HasIndex(request => request.ActiveDatabaseKey).IsUnique();
        requests.HasIndex(request => new { request.Status, request.EligibleAt, request.CreatedAt });
        requests.HasIndex(request => request.RequestedById);
        requests.HasIndex(request => request.ServerId);
        requests.HasIndex(request => request.FinishedAt);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={DbCommon.DbPath}");
    }
}
