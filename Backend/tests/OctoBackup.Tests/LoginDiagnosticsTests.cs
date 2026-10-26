using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Auth.Core.Entities;
using Server.Api.Security;
using Xunit;

public sealed class LoginDiagnosticsTests
{
    [Fact]
    public async Task LoginFailures_AreUniformInBrowser_AndDiagnosedWithoutCredentialsInLogs()
    {
        var logs = new LoginLogs();
        using var factory = new QueueFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var name in new[] { "active", "blocked", "unconfirmed", "locked" })
            {
                var user = new AppUser { UserName = name + "@signin.test", Email = name + "@signin.test",
                    EmailConfirmed = name != "unconfirmed", IsBlocked = name == "blocked" };
                Assert.True((await users.CreateAsync(user, "StrongPassword123!")).Succeeded);
                if (name == "locked")
                    Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15))).Succeeded);
            }
        }
        using var client = host.CreateClient();
        string? expectedTitle = null;
        foreach (var (name, reason) in new[] {
            ("missing", "account_not_found"), ("active", "invalid_credentials"),
            ("blocked", "account_blocked"), ("unconfirmed", "account_unconfirmed"), ("locked", "account_locked_out") })
        {
            var response = await QueueFactory.Post(client, "/api/auth/Login", new { email = name + "@signin.test", password = "RejectedPassword123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("login_rejected", error.GetProperty("code").GetString());
            var title = error.GetProperty("title").GetString();
            expectedTitle ??= title;
            Assert.Equal(expectedTitle, title);
            Assert.DoesNotContain("session has ended", title);
            var trace = error.GetProperty("traceId").GetString();
            Assert.False(string.IsNullOrEmpty(trace));
            Assert.Contains(logs.Messages, entry => entry.Contains(reason) && entry.Contains(trace!));
            Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/auth/GetCurrentUser")).GetProperty("isAuthenticated").GetBoolean());
        }
        Assert.DoesNotContain(logs.Messages, entry => entry.Contains("@signin.test") || entry.Contains("RejectedPassword123!"));
        Assert.Equal(HttpStatusCode.NoContent, (await QueueFactory.Post(client, "/api/auth/Login",
            new { email = " active@signin.test ", password = "StrongPassword123!" })).StatusCode);
    }

    [Fact]
    public async Task BootstrapSettings_DoNotReplaceExistingAccountPasswords()
    {
        using var factory = new QueueFactory();
        await factory.Seed();
        using var scope = factory.Services.CreateScope();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Bootstrap:AdminEmail"] = "admin@queue.test", ["Bootstrap:AdminPassword"] = "ReplacementPassword123!" }).Build();
        await BootstrapAdmin.Initialize(scope.ServiceProvider, settings);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await users.FindByEmailAsync("admin@queue.test");
        Assert.NotNull(admin);
        Assert.True(await users.CheckPasswordAsync(admin, "StrongPassword123!"));
        Assert.False(await users.CheckPasswordAsync(admin, "ReplacementPassword123!"));
    }

    private sealed class LoginLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string category) => new Capture(category, Messages);
        public void Dispose() { }
        private sealed class Capture(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
            {
                if (category.EndsWith("AuthService", StringComparison.Ordinal)) messages.Enqueue(format(state, error));
            }
        }
    }
}
