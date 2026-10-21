using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Core.Entities;
using Modules.Auth.Shared.Static.Entities;

namespace Modules.Auth.Infrastructure.DbContexts;

public sealed class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<TokenModel> UsersTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<IdentityRole>().HasData(
            new IdentityRole
            {
                ConcurrencyStamp = null,
                Id = "D52B5C07-A797-42D7-A2ED-324D0F71AB2F",
                Name = AppRoles.User,
                NormalizedName = AppRoles.User.ToUpper()
            },
            new IdentityRole
            {
                ConcurrencyStamp = null,
                Id = "E21CEFB4-4B40-458C-B7F1-1F9DDBFAA3F9",
                Name = AppRoles.Admin,
                NormalizedName = AppRoles.Admin.ToUpper()
            });

        base.OnModelCreating(builder);
    }
}
