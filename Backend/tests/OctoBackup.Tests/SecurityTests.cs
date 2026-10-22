using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Core.Entities;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Core.StaticClasses;
using Modules.Backup.Infrastructure.DbContexts;
using Modules.Crypto.Shared.Interfaces;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class AppFactory : WebApplicationFactory<Program>
{
    public AppFactory()
    {
        Environment.SetEnvironmentVariable("OCTOBACKUP_DATA", Path.Combine(Path.GetTempPath(), "octobackup-tests-" + Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable("CRYPTO_KEY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:Enabled"] = "false" }));
    }
}

public sealed class SecurityTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Authentication_Csrf_Authorization_And_SessionRevocation()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/servers/GetMyServers")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/auth/Login", new { email = "invalid@example.com", password = "bad" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(anonymous, "/api/BackupsTests/RunTest", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(anonymous, "/api/auth/GetUserToken", new { })).StatusCode);
        Assert.Contains("nosniff", (await anonymous.GetAsync("/api/health")).Headers.GetValues("X-Content-Type-Options"));

        string operatorId;
        var owned = Guid.NewGuid(); var foreign = Guid.NewGuid(); var scheduleId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var admin = new AppUser { UserName = "admin@test.example", Email = "admin@test.example", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(admin, "StrongPassword123!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(admin, "Admin")).Succeeded);
            var user = new AppUser { UserName = "operator@test.example", Email = "operator@test.example", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, "StrongPassword123!")).Succeeded);
            operatorId = user.Id;
            await users.AddToRoleAsync(user, "User");
            var db = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
            foreach (var id in new[] { owned, foreign }) db.DbConnections.Add(new DbServerConnection { Id = id, ServerHost = "db.example", ServerPort = 5432, DbName = "app", DbUser = "backup", DbPasswd = "encrypted", ConnectionName = id == owned ? "Owned" : "Foreign" });
            db.UsersServers.Add(new ServersUsers { ServerId = owned, UserId = Guid.Parse(user.Id) });
            db.Schedules.Add(new BackupSchedule { Id = scheduleId, DbConnectionId = owned, Name = "Original", Configuration = new() { Days = [DayOfWeek.Monday], Times = [new TimeOnly(2, 0)] } });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/api/auth/Login", new { email = "operator@test.example", password = "StrongPassword123!", rememberMe = false })).StatusCode);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/GetCurrentUser")).GetProperty("isAuthenticated").GetBoolean());
        var servers = await client.GetFromJsonAsync<JsonElement>("/api/servers/GetMyServers");
        Assert.Single(servers.EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/backups/PerformBackup", foreign)).StatusCode);
        foreach (var route in new[] { "/api/servers/createserver", "/api/servers/EDITSERVER/" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, route, new { id = foreign, connectionName = "Attack", serverHost = "db.example", serverPort = 5432, dbName = "app", dbUser = "backup", dbPasswd = "secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/api/servers/TestServerConnection", foreign)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/backups/Download?id=" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/administration/GetUsers")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/schedules/EditSchedule", new { id = scheduleId, dbConnectionId = foreign, name = "Attack", selectedDays = new[] { 1 }, selectedTimes = new[] { "02:00:00" }, isEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/schedules/CreateSchedule", new { dbConnectionId = owned, name = "Empty", selectedDays = Array.Empty<int>(), selectedTimes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/schedules/CreateSchedule", new { dbConnectionId = owned, name = "Valid", selectedDays = new[] { 1, 3 }, selectedTimes = new[] { "02:00:00" }, isEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/auth/ChangePassword", new { currentPassword = "StrongPassword123!", newPassword = "AnotherStrong123!", confirmNewPassword = "AnotherStrong123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/servers/GetMyServers")).StatusCode);

        using var adminClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(adminClient, "/api/auth/Login", new { email = "admin@test.example", password = "StrongPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(adminClient, "/api/servers/CreateServer", new { connectionName = "Unapproved", serverHost = "169.254.169.254", serverPort = 3306, dbName = "app", dbUser = "root", dbPasswd = "secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(adminClient, "/api/backups/TestBackup", Guid.NewGuid())).StatusCode);
        using var blockedClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(blockedClient, "/api/auth/Login", new { email = "operator@test.example", password = "AnotherStrong123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(adminClient, "/api/administration/ToggleUserBlockade", operatorId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await blockedClient.GetAsync("/api/servers/GetMyServers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(adminClient, "/api/servers/CascadeDeleteServer", owned)).StatusCode);
        using var checkScope = factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        Assert.False(checkDb.Schedules.Any(s => s.DbConnectionId == owned));
        Assert.False(checkDb.UsersServers.Any(s => s.ServerId == owned));
    }

    [Fact]
    public void Encryption_UsesRandomNonces_AndRejectsTampering()
    {
        using var scope = factory.Services.CreateScope();
        var crypto = scope.ServiceProvider.GetRequiredService<ICryptoService>();
        var a = crypto.Encrypt("database-secret")!; var b = crypto.Encrypt("database-secret")!;
        Assert.StartsWith("gcm1:", a); Assert.NotEqual(a, b); Assert.Equal("database-secret", crypto.Decrypt(a));
        // The old deployment format remains readable so upgrading does not lose credentials.
        using var legacy = Aes.Create();
        legacy.Key = Convert.FromBase64String(Environment.GetEnvironmentVariable("CRYPTO_KEY")!);
        legacy.GenerateIV();
        var plain = System.Text.Encoding.UTF8.GetBytes("legacy-secret");
        var encrypted = legacy.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
        Assert.Equal("legacy-secret", crypto.Decrypt(Convert.ToBase64String(legacy.IV.Concat(encrypted).ToArray())));
        var bytes = Convert.FromBase64String(a[5..]); bytes[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => crypto.Decrypt("gcm1:" + Convert.ToBase64String(bytes)));
    }

    [Theory]
    [InlineData("..")][InlineData("../escape")][InlineData("C:\\escape")][InlineData("/absolute")]
    public void BackupPath_RejectsTraversal(string segment) => Assert.Throws<ArgumentException>(() => SafeBackupPath.Segment(segment));
}
