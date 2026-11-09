using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Core.Entities;
using Modules.Auth.Shared.Static.Entities;

namespace Server.Api.Security;

public static class BootstrapAdmin
{
    public static async Task Initialize(IServiceProvider services, IConfiguration config)
    {
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        var users = services.GetRequiredService<UserManager<AppUser>>();
        if (await users.Users.AnyAsync())
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Server.Api.Security.BootstrapAdmin")
                .LogInformation("Existing user store detected. Bootstrap credentials are not applied; changing bootstrap settings does not reset existing accounts.");
            return;
        }
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException("Bootstrap admin creation failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        var roles = await users.AddToRolesAsync(user, [AppRoles.Admin, AppRoles.User]);
        if (!roles.Succeeded) throw new InvalidOperationException("Bootstrap admin role assignment failed.");
    }
}
