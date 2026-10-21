using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Core.Entities;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Shared.Settings;
using Server.Api.Configuration;
using Xunit;

public sealed class ApplicationSettingsStoreTests
{
    [Fact]
    public async Task SettingsAreDurable_RejectStaleWrites_AndOverrideOnlyRuntimeDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "octobackup-settings-" + Guid.NewGuid());
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:AllowedDatabaseHosts:0"] = "LOCALHOST", ["AuthSettings:EnableRegisterModule"] = "true" });
        using var store = new FileApplicationSettings(config, root);
        var initial = store.Current;
        Assert.Equal(["localhost"], initial.AllowedDatabaseHosts);
        var saved = await store.Save(initial with { AllowedDatabaseHosts = ["DB.Example.", "db.example", "::1"], DefaultRetentionDays = 12, BackupQueueDelaySeconds = 45, BackupQueueSpacingSeconds = 10, UpdatedBy = "spoofed" }, "admin-id");
        Assert.NotNull(saved);
        Assert.Equal("admin-id", saved.UpdatedBy);
        Assert.NotEqual(initial.Revision, saved.Revision);
        Assert.Null(await store.Save(initial, "second-admin"));
        // Callers cannot mutate the live allowlist through a returned snapshot.
        saved.AllowedDatabaseHosts[0] = "tampered.example";
        Assert.DoesNotContain("tampered.example", store.Current.AllowedDatabaseHosts);
        using var restarted = new FileApplicationSettings(new ConfigurationManager(), root);
        Assert.Equal(store.Current.Revision, restarted.Current.Revision);
        Assert.Equal(store.Current.RegistrationEnabled, restarted.Current.RegistrationEnabled);
        Assert.Equal(["::1", "db.example"], restarted.Current.AllowedDatabaseHosts);
        Assert.Equal(12, restarted.Current.DefaultRetentionDays);
        Assert.Equal(45, restarted.Current.BackupQueueDelaySeconds);
        Assert.Equal(10, restarted.Current.BackupQueueSpacingSeconds);
    }

    [Theory]
    [InlineData("https://db.example")]
    [InlineData("db.example:5432")]
    [InlineData("*.example")]
    [InlineData("db.example/path")]
    [InlineData("db..example")]
    [InlineData("user@db.example")]
    [InlineData("db.example\nother.example")]
    public void HostPolicyRejectsUrlsWildcardsPortsAndAmbiguousInput(string host)
        => Assert.Throws<ArgumentException>(() => FileApplicationSettings.Validate(new ApplicationSettings { AllowedDatabaseHosts = [host] }));

    [Fact]
    public async Task InvalidSettingsNeverChangeDurableOrLiveState()
    {
        var root = Path.Combine(Path.GetTempPath(), "octobackup-settings-" + Guid.NewGuid());
        using var store = new FileApplicationSettings(new ConfigurationManager(), root);
        var original = store.Current;
        await Assert.ThrowsAsync<ArgumentException>(() => store.Save(original with { DefaultRetentionDays = 0 }, "admin"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Save(original with { SchedulePollSeconds = 1 }, "admin"));
        foreach (var delay in new[] { 4, 301 })
            await Assert.ThrowsAsync<ArgumentException>(() => store.Save(original with { BackupQueueDelaySeconds = delay }, "admin"));
        foreach (var spacing in new[] { 0, 301 })
            await Assert.ThrowsAsync<ArgumentException>(() => store.Save(original with { BackupQueueSpacingSeconds = spacing }, "admin"));
        Assert.Equal(original.Revision, store.Current.Revision);
        Assert.False(File.Exists(Path.Combine(root, "Config", "application-settings.json")));
    }

    [Fact]
    public void SshConnectionsRequireEveryDestination_AndHostComparisonIsCanonical()
    {
        var settings = FileApplicationSettings.Validate(new ApplicationSettings { AllowedDatabaseHosts = ["DB.EXAMPLE.", "gateway.example", "::1"] });
        Assert.True(DatabaseHostPolicy.AllowsConnection(settings, "db.example", true, "gateway.example", "0:0:0:0:0:0:0:1"));
        Assert.False(DatabaseHostPolicy.AllowsConnection(settings, "db.example", true, "other.example", "::1"));
        Assert.False(DatabaseHostPolicy.AllowsConnection(settings, "db.example", true, "gateway.example", "other.example"));
        Assert.False(DatabaseHostPolicy.AllowsConnection(settings, "db.example", true, null, null));
    }

    [Fact]
    public void InvalidPersistedSettingsFailClosed()
    {
        var root = Path.Combine(Path.GetTempPath(), "octobackup-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        File.WriteAllText(Path.Combine(root, "Config", "application-settings.json"), "{broken");
        Assert.Throws<JsonException>(() => new FileApplicationSettings(new ConfigurationManager(), root));
    }
}

public sealed class ApplicationSettingsApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task AdminSettingsControlHostAccessRegistrationAndRetention_WithoutRestart()
    {
        using var admin = factory.CreateClient();
        using var user = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        string adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var a = new AppUser { Email = "settings-admin@test.example", UserName = "settings-admin@test.example", EmailConfirmed = true };
            Assert.True((await manager.CreateAsync(a, "StrongPassword123!")).Succeeded);
            await manager.AddToRoleAsync(a, "Admin"); adminId = a.Id;
            var u = new AppUser { Email = "settings-user@test.example", UserName = "settings-user@test.example", EmailConfirmed = true };
            Assert.True((await manager.CreateAsync(u, "StrongPassword123!")).Succeeded);
            await manager.AddToRoleAsync(u, "User");
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/administration/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(admin, "/api/auth/Login", new { email = "settings-admin@test.example", password = "StrongPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(user, "/api/auth/Login", new { email = "settings-user@test.example", password = "StrongPassword123!" })).StatusCode);
        var initial = (await admin.GetFromJsonAsync<ApplicationSettings>("/api/administration/settings"))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/ADMINISTRATION/SETTINGS/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(user, "/api/administration/settings", initial)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/administration/settings", initial)).StatusCode);
        var connection = new { connectionName = "Settings test", serverHost = "db.settings.example", serverPort = 5432, dbType = 1, dbName = "app", dbUser = "backup", dbPasswd = "secret" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/servers/CreateServer", connection)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "/api/administration/settings", initial with { AllowedDatabaseHosts = ["*"] })).StatusCode);
        var response = await Post(admin, "/api/administration/settings", initial with { AllowedDatabaseHosts = ["db.settings.example"], RegistrationEnabled = false, AutoConfirmAccounts = false, DefaultRetentionDays = 11, SchedulePollSeconds = 30 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<ApplicationSettings>())!;
        Assert.Equal(adminId, saved.UpdatedBy);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(admin, "/api/administration/settings", initial)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(admin, "/api/servers/CreateServer", connection)).StatusCode);
        Guid serverId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            serverId = db.DbConnections.Single(s => s.ConnectionName == "Settings test").Id;
            Assert.Equal(11, db.Configurations.Single(c => c.ServerId == serverId).TimeInDaysToHoldBackups);
        }
        var signup = new { email = "signup@test.example", password = "StrongPassword123!", passwordConfirm = "StrongPassword123!" };
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(anonymous, "/api/auth/Register", signup)).StatusCode);
        response = await Post(admin, "/api/administration/settings", saved with { RegistrationEnabled = true });
        saved = (await response.Content.ReadFromJsonAsync<ApplicationSettings>())!;
        Assert.Equal(HttpStatusCode.Created, (await Post(anonymous, "/api/auth/Register", signup)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, "/api/auth/Login", signup)).StatusCode);
        string pendingUserId;
        using (var scope = factory.Services.CreateScope())
            pendingUserId = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(signup.email))!.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(user, "/api/administration/ConfirmUser", pendingUserId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(admin, "/api/administration/ConfirmUser", pendingUserId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(anonymous, "/api/auth/Login", signup)).StatusCode);
        response = await Post(admin, "/api/administration/settings", saved with { AllowedDatabaseHosts = [], AutoConfirmAccounts = true });
        saved = (await response.Content.ReadFromJsonAsync<ApplicationSettings>())!;
        Assert.Empty(saved.AllowedDatabaseHosts);
        foreach (var route in new[] { "/api/servers/TestServerConnection", "/api/backups/PerformBackup" })
        {
            var denied = await Post(admin, route, serverId);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.Contains("Host is not approved", await denied.Content.ReadAsStringAsync());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            var dueDate = DateTime.Now.AddMinutes(-5);
            var schedule = new BackupSchedule { Name = "Revoked host schedule", DbConnectionId = serverId, IsEnabled = true, NextBackupDate = dueDate,
                Configuration = new() { Days = [DayOfWeek.Monday], Times = [new TimeOnly(2, 0)] } };
            db.Schedules.Add(schedule); await db.SaveChangesAsync();
            var result = await scope.ServiceProvider.GetRequiredService<Modules.Backup.Application.Services.BackupQueueService>().EnqueueDueSchedules();
            Assert.Equal(0, result);
            Assert.Equal(dueDate, schedule.NextBackupDate);
            Assert.Empty(db.Backups);
        }
        var policy = await anonymous.GetFromJsonAsync<JsonElement>("/api/auth/registration-policy");
        Assert.True(policy.GetProperty("autoConfirm").GetBoolean());
        Assert.False(policy.TryGetProperty("allowedDatabaseHosts", out _));
        using var newSignup = factory.CreateClient();
        var automaticSignup = new { email = "auto-signup@test.example", password = "StrongPassword123!", passwordConfirm = "StrongPassword123!" };
        Assert.Equal(HttpStatusCode.Created, (await Post(newSignup, "/api/auth/Register", automaticSignup)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(newSignup, "/api/auth/Login", automaticSignup)).StatusCode);
    }
}
