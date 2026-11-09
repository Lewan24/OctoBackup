using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Core.Entities;
using Xunit;

public sealed class DeploymentSecurityTests
{
    [Theory]
    [InlineData(false, "https://localhost", true)]
    [InlineData(true, "http://localhost", false)]
    public async Task Production_Login_UsesExplicitCookiePolicy_AndStillRequiresCsrf(bool allowHttp, string origin, bool secure)
    {
        using var deployment = new DeploymentEnvironment(new Dictionary<string, string?> {
            ["Security__AllowInsecureHttp"] = allowHttp.ToString(), ["ReverseProxy__Enabled"] = "false" });
        using var factory = new QueueFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:AllowInsecureHttp"] = allowHttp.ToString(), ["ReverseProxy:Enabled"] = "false" }));
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin) });
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await users.CreateAsync(new AppUser {
                UserName = "deployment@test.example", Email = "deployment@test.example", EmailConfirmed = true
            }, "StrongPassword123!")).Succeeded);
        }

        var csrfResponse = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        Assert.True(csrfResponse.Headers.CacheControl!.NoCache);
        Assert.True(csrfResponse.Headers.CacheControl.NoStore);
        Assert.Contains("no-cache", csrfResponse.Headers.Pragma.Select(h => h.Name));
        var cookie = Assert.Single(csrfResponse.Headers.GetValues("Set-Cookie"));
        Assert.Equal(secure, cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        var token = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var login = new { email = "deployment@test.example", password = "StrongPassword123!" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/Login", login)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/Login") { Content = JsonContent.Create(login) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var session = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("OctoBackup.Session="));
        Assert.Equal(secure, session.Contains("; secure", StringComparison.OrdinalIgnoreCase));
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/auth/GetCurrentUser")).GetProperty("isAuthenticated").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecureProduction_OnlyAcceptsForwardingFromConfiguredProxy(bool trusted)
    {
        using var deployment = new DeploymentEnvironment(new Dictionary<string, string?> {
            ["Security__AllowInsecureHttp"] = "false", ["ReverseProxy__Enabled"] = "true",
            ["ReverseProxy__KnownProxies__0"] = "192.0.2.10" });
        using var factory = new QueueFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Security:AllowInsecureHttp"] = "false", ["ReverseProxy:Enabled"] = "true",
                ["ReverseProxy:KnownProxies:0"] = "192.0.2.10" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(trusted)));
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.20");
        var csrf = await client.GetAsync("/api/auth/csrf");
        if (trusted)
        {
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            Assert.Contains("; secure", Assert.Single(csrf.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
            return;
        }
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        Assert.Contains("HTTPS", await csrf.Content.ReadAsStringAsync());
        Assert.False(csrf.Headers.Contains("Set-Cookie"));

        var rejected = await client.PostAsJsonAsync("/api/auth/Login", new { email = "unused", password = "unused" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("csrf_cookie_missing", error.GetProperty("code").GetString());
        Assert.Contains("HTTPS", error.GetProperty("title").GetString());
    }

    private sealed class RemoteAddressFilter(bool trusted) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
            app.Use((context, continuation) => {
                // TestServer has no socket remote address; model a real connection
                // so forwarded-header middleware can enforce its proxy allowlist.
                context.Connection.RemoteIpAddress = IPAddress.Parse(trusted ? "192.0.2.10" : "198.51.100.10");
                return continuation();
            });
            next(app);
        };
    }

    // Startup transport options are read before WebApplicationFactory's later
    // ConfigureAppConfiguration callbacks. Exercise actual deployment inputs.
    private sealed class DeploymentEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> previous;
        public DeploymentEnvironment(Dictionary<string, string?> values)
        {
            previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach (var pair in values) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        public void Dispose()
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
