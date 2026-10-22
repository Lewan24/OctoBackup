using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Shared.Dtos;
using Modules.Backup.Shared.Enums;
using Modules.Shared.Common;
using Server.Api.Configuration;
using Xunit;

public sealed class RestoreFixtureRunner : IRestoreTestRunner
{
    public int Calls { get; private set; }
    public int Cleanups { get; private set; }
    public bool Fail { get; set; }
    public bool FailCleanup { get; set; }
    public async Task<RestorePreview> Run(BackupTest test, string path, CancellationToken token)
    {
        Calls++;
        Assert.True(File.Exists(path));
        await Task.Yield();
        if (Fail) throw new RestoreTestException("Restore failed in fixture.");
        return new(1, [new("public", "probe", ["id", "value"], [new string?[] { "1", "<script>sample</script>" }])], "Fixture sample");
    }
    public Task Cleanup(Guid id, CancellationToken token) { Cleanups++; if (FailCleanup) throw new InvalidOperationException("private cleanup output"); return Task.CompletedTask; }
    public Task CleanupOrphans(CancellationToken token) => Task.CompletedTask;
}

public sealed class RestoreFactory(bool enabled = true) : QueueFactory
{
    public RestoreFixtureRunner Runner { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:Enabled"] = enabled.ToString() }));
        builder.ConfigureServices(s => { s.RemoveAll<IRestoreTestRunner>(); s.AddSingleton<IRestoreTestRunner>(Runner); });
    }
    public async Task<Guid> Artifact()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        Directory.CreateDirectory(DbCommon.BackupDirectory);
        var path = Path.Combine(DbCommon.BackupDirectory, Guid.NewGuid() + ".zip");
        await File.WriteAllTextAsync(path, "fixture artifact");
        var backup = new PerformedBackup { Id = Guid.NewGuid(), Name = "Restore fixture", FilePath = path, ServerConnectionId = ServerId, CreatedOn = DateTime.Now };
        db.Backups.Add(backup); await db.SaveChangesAsync(); return backup.Id;
    }
    public async Task<bool> Restore()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RestoreTestService>().ProcessNext(CancellationToken.None);
    }
    public async Task Auto(bool enabled)
    {
        using var scope = Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<FileApplicationSettings>();
        Assert.NotNull(await settings.Save(settings.Current with { AutomaticRestoreTests = enabled }, AdminId));
    }
}

public sealed class RestoreTestTests
{
    [Fact]
    public async Task MissingReport_IsExplicitJsonNull_AndDisabledRuntimeCannotBeEnabledFromAdminToggle()
    {
        using var factory = new RestoreFactory(enabled: false); await factory.Seed(); var id = await factory.Artifact();
        using var owner = await factory.Login("owner"); using var admin = await factory.Login("admin");
        var response = await owner.GetAsync("/api/backups/RestoreReport?id=" + id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("null", await response.Content.ReadAsStringAsync());
        Assert.Null(await response.Content.ReadFromJsonAsync<RestoreReportDto>());
        var availability = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/backups/RestoreAvailability");
        Assert.False(availability.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        var settings = (await admin.GetFromJsonAsync<Modules.Shared.Settings.ApplicationSettings>("/api/administration/settings"))!;
        response = await QueueFactory.Post(admin, "/api/administration/settings", settings with { AutomaticRestoreTests = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RestoreTests:Enabled", await response.Content.ReadAsStringAsync());
        Assert.False((await admin.GetFromJsonAsync<Modules.Shared.Settings.ApplicationSettings>("/api/administration/settings"))!.AutomaticRestoreTests);
    }

    [Fact]
    public async Task CleanupFailure_PreservesOriginalRestoreReason_WithoutLeakingExceptionOutput()
    {
        using var factory = new RestoreFactory(); await factory.Seed(); var id = await factory.Artifact();
        using var owner = await factory.Login("owner");
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        factory.Runner.Fail = true; factory.Runner.FailCleanup = true;
        Assert.True(await factory.Restore());
        var report = (await owner.GetFromJsonAsync<RestoreReportDto>("/api/backups/RestoreReport?id=" + id))!;
        Assert.Equal(ETestStatus.Failure, report.Status);
        Assert.Contains("Restore failed in fixture.", report.Message);
        Assert.Contains("cleanup failed", report.Message);
        Assert.DoesNotContain("private cleanup output", report.Message);
    }

    [Fact]
    public async Task ManualTest_RequiresAccessAndCsrf_Deduplicates_SavesPreview_AndAllowsRetry()
    {
        using var factory = new RestoreFactory(); await factory.Seed(); var id = await factory.Artifact();
        using var owner = await factory.Login("owner"); using var outsider = await factory.Login("outsider");
        Assert.Equal(HttpStatusCode.Forbidden, (await QueueFactory.Post(outsider, "/api/backups/TestBackup", id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/backups/RestoreReport?id=" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/backups/TestBackup", id)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        Assert.True(await factory.Restore()); Assert.False(await factory.Restore());
        var response = await owner.GetAsync("/api/backups/RestoreReport?id=" + id);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var report = (await response.Content.ReadFromJsonAsync<RestoreReportDto>())!;
        Assert.Equal(ETestStatus.Success, report.Status); Assert.False(report.Automatic);
        Assert.Single(report.Preview!.Tables); Assert.Equal(1, factory.Runner.Cleanups);
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        factory.Runner.Fail = true; Assert.True(await factory.Restore());
        report = (await owner.GetFromJsonAsync<RestoreReportDto>("/api/backups/RestoreReport?id=" + id))!;
        Assert.Equal(ETestStatus.Failure, report.Status); Assert.Null(report.Preview); Assert.Equal(2, factory.Runner.Cleanups);
    }

    [Fact]
    public async Task AutomaticTests_CommitWithBackup_DisablingSkipsWaiting_ManualRemainsAvailable()
    {
        using var factory = new RestoreFactory(); await factory.Seed(); using var owner = await factory.Login("owner");
        await factory.Auto(true); var request = await factory.Enqueue(owner); factory.Clock.Advance(21);
        Assert.True(await factory.Process()); var stored = await factory.Read(request.Id);
        Assert.NotNull(stored.BackupId);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var auto = await db.BackupsTests.AsNoTracking().SingleAsync(t => t.BackupId == stored.BackupId);
        Assert.True(auto.Automatic); Assert.Equal(ETestStatus.Queued, auto.Status);
        Assert.Equal(DatabaseType.MySql, auto.DatabaseType);
        await factory.Auto(false); Assert.True(await factory.Restore());
        Assert.Equal(0, factory.Runner.Calls);
        Assert.Equal(ETestStatus.Skipped, (await db.BackupsTests.AsNoTracking().SingleAsync(t => t.Id == auto.Id)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/TestBackup", stored.BackupId!)).StatusCode);
        Assert.True(await factory.Restore()); Assert.Equal(1, factory.Runner.Calls);
    }

    [Fact]
    public async Task RevokedAccess_PreventsExecution_AndRestartMarksRunningInterrupted()
    {
        using var factory = new RestoreFactory(); await factory.Seed(); var id = await factory.Artifact();
        using var owner = await factory.Login("owner");
        Assert.Equal(HttpStatusCode.Accepted, (await QueueFactory.Post(owner, "/api/backups/TestBackup", id)).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        await db.UsersServers.Where(s => s.UserId == Guid.Parse(factory.OwnerId)).ExecuteDeleteAsync();
        Assert.True(await factory.Restore()); Assert.Equal(0, factory.Runner.Calls);
        var test = await db.BackupsTests.SingleAsync(t => t.BackupId == id);
        Assert.Equal(ETestStatus.Failure, test.Status); Assert.Null(test.ActiveBackupId);
        test.Status = ETestStatus.Running; test.ActiveBackupId = id; await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<RestoreTestService>().Recover(CancellationToken.None);
        await db.Entry(test).ReloadAsync(); Assert.Equal(ETestStatus.Interrupted, test.Status); Assert.Null(test.ActiveBackupId);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/backups/RestoreReport?id=" + id)).StatusCode);
    }
}
