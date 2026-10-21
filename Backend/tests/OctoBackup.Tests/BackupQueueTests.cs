using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Core.Entities;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Application.Services;
using Modules.Backup.Application.Workers;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Shared.Common;
using Server.Api.Configuration;
using Xunit;

public sealed class QueueClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
}

public sealed class QueueTestExecutor : IBackupExecutor
{
    public bool Block { get; set; }
    public int Calls { get; private set; }
    public string? PathCreated { get; private set; }
    public Func<Task>? BeforeReturn { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<BackupArtifact> Execute(BackupRequest request, CancellationToken token)
    {
        Calls++;
        Directory.CreateDirectory(DbCommon.BackupDirectory);
        PathCreated = Path.Combine(DbCommon.BackupDirectory, Guid.NewGuid() + ".zip");
        await File.WriteAllTextAsync(PathCreated, "queue fixture backup", token);
        Started.TrySetResult();
        try
        {
            if (Block) await Task.Delay(Timeout.Infinite, token);
            if (BeforeReturn is not null) await BeforeReturn();
            return new(Guid.NewGuid(), "Queue fixture", PathCreated);
        }
        catch { File.Delete(PathCreated); throw; }
    }
}

public sealed class QueueFactory : WebApplicationFactory<Program>
{
    public QueueClock Clock { get; } = new();
    public QueueTestExecutor Executor { get; } = new();
    public Guid ServerId { get; } = Guid.NewGuid();
    public string OwnerId { get; private set; } = "";
    public string AdminId { get; private set; } = "";
    public QueueFactory()
    {
        Environment.SetEnvironmentVariable("OCTOBACKUP_DATA", Path.Combine(Path.GetTempPath(), "octobackup-queue-" + Guid.NewGuid()));
        Environment.SetEnvironmentVariable("CRYPTO_KEY", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(BackupWorker)).ToList()) services.Remove(service);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IBackupExecutor>(); services.AddSingleton<IBackupExecutor>(Executor);
        });
    }
    public async Task Seed()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var name in new[] { "admin", "owner", "peer", "outsider" })
        {
            var user = new AppUser { UserName = name + "@queue.test", Email = name + "@queue.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, "StrongPassword123!")).Succeeded);
            await users.AddToRoleAsync(user, name == "admin" ? "Admin" : "User");
            if (name == "owner") OwnerId = user.Id;
            if (name == "admin") AdminId = user.Id;
            if (name is "owner" or "peer")
                scope.ServiceProvider.GetRequiredService<BackupsDbContext>().UsersServers.Add(new ServersUsers { ServerId = ServerId, UserId = Guid.Parse(user.Id) });
        }
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        db.DbConnections.Add(new DbServerConnection { Id = ServerId, ConnectionName = "Queue database", DbName = "queue_data",
            ServerHost = "localhost", ServerPort = 3306, DbUser = "backup", DbPasswd = "not-used-by-fixture" });
        await db.SaveChangesAsync();
    }
    public async Task<HttpClient> Login(string name)
    {
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/api/auth/Login", new { email = name + "@queue.test", password = "StrongPassword123!" })).StatusCode);
        return client;
    }
    public static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    public async Task<BackupRequestDto> Enqueue(HttpClient client)
    {
        var response = await Post(client, "/api/backups/PerformBackup", ServerId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BackupRequestDto>())!;
    }
    public async Task<BackupRequest> Read(Guid id)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().BackupRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }
    public async Task<bool> Process()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BackupQueueProcessor>().ProcessNext();
    }
}

public sealed class BackupQueueTests
{
    [Fact]
    public async Task MalformedSchedulesDoNotBlockValidWork_AndCancellingJobsDoNotConsumeOccurrences()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner");
        var request = await factory.Enqueue(owner);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var due = factory.Clock.GetLocalNow().DateTime.AddMinutes(-1);
        db.Schedules.Add(new BackupSchedule { Name = "Broken", DbConnectionId = factory.ServerId, NextBackupDate = due, ConfigurationJson = "invalid json" });
        var valid = new BackupSchedule { Name = "Valid", DbConnectionId = factory.ServerId, NextBackupDate = due,
            Configuration = new() { Days = [DayOfWeek.Monday], Times = [new TimeOnly(2, 0)] } };
        db.Schedules.Add(valid); await db.SaveChangesAsync();
        await db.BackupRequests.Where(r => r.Id == request.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Cancelling"));
        var service = scope.ServiceProvider.GetRequiredService<BackupQueueService>();
        Assert.Equal(0, await service.EnqueueDueSchedules()); Assert.Equal(due, valid.NextBackupDate);
        await db.BackupRequests.Where(r => r.Id == request.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Cancelled").SetProperty(r => r.ActiveDatabaseKey, (string?)null));
        db.ChangeTracker.Clear();
        Assert.Equal(1, await service.EnqueueDueSchedules());
        Assert.True((await db.Schedules.SingleAsync(s => s.Id == valid.Id)).NextBackupDate > due);
    }

    [Fact]
    public async Task DifferentDatabasesRunSeriallyWithSpacing_AndCancelledWaitingRequestsNeverExecute()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var admin = await factory.Login("admin");
        var first = await factory.Enqueue(admin);
        Guid otherId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            var other = (await db.DbConnections.AsNoTracking().SingleAsync()) with { Id = Guid.NewGuid(), DbName = "other_data", ConnectionName = "Second" };
            otherId = other.Id; db.DbConnections.Add(other); await db.SaveChangesAsync();
        }
        var response = await QueueFactory.Post(admin, "/api/backups/PerformBackup", otherId);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var second = (await response.Content.ReadFromJsonAsync<BackupRequestDto>())!;
        factory.Clock.Advance(20); Assert.True(await factory.Process()); Assert.False(await factory.Process());
        factory.Clock.Advance(4); Assert.False(await factory.Process());
        factory.Clock.Advance(1); Assert.True(await factory.Process()); Assert.Equal(2, factory.Executor.Calls);
        Assert.Equal("Succeeded", (await factory.Read(second.Id)).Status);
        var cancelled = await factory.Enqueue(admin);
        await QueueFactory.Post(admin, "/api/backup-queue/Cancel", cancelled.Id);
        factory.Clock.Advance(30); Assert.False(await factory.Process()); Assert.Equal(2, factory.Executor.Calls);
    }

    [Fact]
    public async Task RequestsAreDurableDelayedAndPublishedAtomically_WithSpacing()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var client = await factory.Login("owner");
        var request = await factory.Enqueue(client);
        Assert.Equal("Queued", (await factory.Read(request.Id)).Status);
        Assert.Equal(TimeSpan.FromSeconds(20), request.EligibleAt - request.CreatedAt);
        Assert.False(await factory.Process());
        factory.Clock.Advance(19); Assert.False(await factory.Process());
        factory.Clock.Advance(1); Assert.True(await factory.Process());
        var complete = await factory.Read(request.Id);
        Assert.Equal("Succeeded", complete.Status); Assert.Null(complete.ActiveDatabaseKey); Assert.NotNull(complete.BackupId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            var backup = await db.Backups.SingleAsync(); Assert.Equal(complete.BackupId, backup.Id); Assert.True(File.Exists(backup.FilePath));
        }
        // A new request keeps its own grace period, and completion spacing survives
        // separate scopes/process iterations through the durable finished timestamp.
        var again = await factory.Enqueue(client);
        factory.Clock.Advance(20); Assert.True(await factory.Process());
        Assert.Equal("Succeeded", (await factory.Read(again.Id)).Status);
        Assert.Equal(2, factory.Executor.Calls);
    }

    [Fact]
    public async Task ConcurrentRequestsAndAliasConnectionsCannotDuplicateAnActiveDatabase()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner"); using var admin = await factory.Login("admin");
        var results = await Task.WhenAll(QueueFactory.Post(owner, "/api/backups/PerformBackup", factory.ServerId), QueueFactory.Post(admin, "/api/backups/PerformBackup", factory.ServerId));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var alias = (await db.DbConnections.AsNoTracking().SingleAsync()) with { Id = Guid.NewGuid(), ConnectionName = "Alias", DbUser = "other" };
        db.DbConnections.Add(alias); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await QueueFactory.Post(admin, "/api/backups/PerformBackup", alias.Id)).StatusCode);
        var active = await db.BackupRequests.AsNoTracking().SingleAsync();
        db.BackupRequests.Add(new BackupRequest { ServerId = alias.Id, ServerName = "Alias", DatabaseName = alias.DbName, TargetKey = active.TargetKey, ActiveDatabaseKey = active.TargetKey });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OwnershipAdminAuthorizationCsrfAndQueuedRemovalAreEnforced()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner"); using var peer = await factory.Login("peer");
        using var outsider = await factory.Login("outsider"); using var admin = await factory.Login("admin"); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/backup-queue")).StatusCode);
        var request = await factory.Enqueue(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/backup-queue/Cancel", request.Id)).StatusCode);
        var peerView = (await peer.GetFromJsonAsync<BackupQueueDto>("/api/backup-queue"))!;
        Assert.Equal("Another operator", Assert.Single(peerView.Requests).RequestedBy); Assert.False(peerView.Requests[0].CanCancel);
        Assert.Empty((await outsider.GetFromJsonAsync<BackupQueueDto>("/api/backup-queue"))!.Requests);
        foreach (var actor in new[] { peer, outsider })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await QueueFactory.Post(actor, "/api/backup-queue/Cancel", request.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await QueueFactory.Post(actor, "/api/backup-queue/Delete", request.Id)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(admin, "/api/BACKUP-QUEUE/CANCEL/", request.Id)).StatusCode);
        Assert.Equal("Cancelled", (await factory.Read(request.Id)).Status);
        var removable = await factory.Enqueue(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(owner, "/api/backup-queue/Delete", removable.Id)).StatusCode);
        var removed = await factory.Read(removable.Id); Assert.True(removed.IsHidden); Assert.Equal("Cancelled", removed.Status); Assert.Null(removed.ActiveDatabaseKey);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<BackupQueueDto>("/api/backup-queue"))!.Requests, r => r.Id == removable.Id);
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/PerformBackup", factory.ServerId)).StatusCode);
    }

    [Fact]
    public async Task RunningCancellationStopsExecutionAndRetainsDuplicateGuardUntilAcknowledged()
    {
        using var factory = new QueueFactory(); await factory.Seed(); factory.Executor.Block = true; using var owner = await factory.Login("owner"); using var admin = await factory.Login("admin");
        var request = await factory.Enqueue(owner); factory.Clock.Advance(20);
        var processing = factory.Process(); await factory.Executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Running", (await factory.Read(request.Id)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await QueueFactory.Post(owner, "/api/backups/PerformBackup", factory.ServerId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await QueueFactory.Post(owner, "/api/backup-queue/Delete", request.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QueueFactory.Post(admin, "/api/servers/CascadeDeleteServer", factory.ServerId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(owner, "/api/backup-queue/Cancel", request.Id)).StatusCode);
        Assert.True(await processing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Cancelled", (await factory.Read(request.Id)).Status); Assert.False(File.Exists(factory.Executor.PathCreated));
        using var scope = factory.Services.CreateScope(); Assert.Empty(scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Backups);
        Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(owner, "/api/backup-queue/Delete", request.Id)).StatusCode);
    }

    [Fact]
    public async Task CancellationWinningPublicationNeverCreatesBackupHistoryOrKeepsArtifact()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner");
        var request = await factory.Enqueue(owner);
        factory.Executor.BeforeReturn = async () => Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(owner, "/api/backup-queue/Cancel", request.Id)).StatusCode);
        factory.Clock.Advance(20); Assert.True(await factory.Process());
        Assert.Equal("Cancelled", (await factory.Read(request.Id)).Status); Assert.False(File.Exists(factory.Executor.PathCreated));
        using var scope = factory.Services.CreateScope(); Assert.Empty(scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Backups);
    }

    [Fact]
    public async Task SchedulesCoalescePersistAndAdvanceAtAcceptance_CancelSkipsThatOccurrence()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            foreach (var name in new[] { "Schedule A", "Schedule B" }) db.Schedules.Add(new BackupSchedule { Name = name, DbConnectionId = factory.ServerId,
                NextBackupDate = factory.Clock.GetLocalNow().DateTime.AddMinutes(-1), Configuration = new() { Days = [DayOfWeek.Monday], Times = [new TimeOnly(2, 0)] } });
            await db.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<BackupQueueService>();
            Assert.Equal(1, await service.EnqueueDueSchedules()); Assert.Equal(0, await service.EnqueueDueSchedules());
            Assert.All(await db.Schedules.ToListAsync(), s => Assert.True(s.NextBackupDate > factory.Clock.GetLocalNow().DateTime));
        }
        var view = (await owner.GetFromJsonAsync<BackupQueueDto>("/api/backup-queue"))!; var request = Assert.Single(view.Requests);
        Assert.Equal("Scheduled", request.Source); Assert.True(request.CanCancel);
        Assert.Equal(2, JsonSerializer.Deserialize<Guid[]>((await factory.Read(request.Id)).ScheduleIdsJson)!.Length);
        await QueueFactory.Post(owner, "/api/backup-queue/Cancel", request.Id);
        using var check = factory.Services.CreateScope(); Assert.Equal(0, await check.ServiceProvider.GetRequiredService<BackupQueueService>().EnqueueDueSchedules());
    }

    [Fact]
    public async Task StartupRecoveryPreservesWaitingRequests_AndReleasesInterruptedRequests()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner");
        var request = await factory.Enqueue(owner);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            await db.BackupRequests.Where(r => r.Id == request.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, "Running").SetProperty(r => r.StartedAt, factory.Clock.GetUtcNow().ToUnixTimeMilliseconds()));
            await scope.ServiceProvider.GetRequiredService<BackupQueueProcessor>().RecoverInterrupted();
        }
        Assert.Equal("Interrupted", (await factory.Read(request.Id)).Status);
        var waiting = await factory.Enqueue(owner);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<BackupQueueProcessor>().RecoverInterrupted();
        var reloaded = await factory.Read(waiting.Id); Assert.Equal("Queued", reloaded.Status);
        Assert.Equal(waiting.EligibleAt.ToUnixTimeMilliseconds(), reloaded.EligibleAt); Assert.Equal(0, factory.Executor.Calls);
    }

    [Fact]
    public async Task AccessAndHostPolicyAreRecheckedBeforeBackgroundExecution()
    {
        using var factory = new QueueFactory(); await factory.Seed(); using var owner = await factory.Login("owner"); using var admin = await factory.Login("admin");
        var request = await factory.Enqueue(owner);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().UsersServers.Where(s => s.UserId == Guid.Parse(factory.OwnerId)).ExecuteDeleteAsync();
        factory.Clock.Advance(20); await factory.Process(); Assert.Equal("Failed", (await factory.Read(request.Id)).Status); Assert.Equal(0, factory.Executor.Calls);
        var privileged = await factory.Enqueue(admin);
        using (var scope = factory.Services.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<FileApplicationSettings>();
            await settings.Save(settings.Current with { AllowedDatabaseHosts = [] }, factory.AdminId);
        }
        factory.Clock.Advance(20); await factory.Process(); Assert.Equal("Failed", (await factory.Read(privileged.Id)).Status); Assert.Equal(0, factory.Executor.Calls);
    }
}
