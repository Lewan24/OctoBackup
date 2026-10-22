using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Infrastructure.DbContexts;

using Modules.Shared.Common;

namespace Modules.Auth.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
    {
        services.AddDbContext<AppIdentityDbContext>(options =>
        {
            DbCommon.CreateDbDirectoryIfNotExists();
            options.UseSqlite($"Data Source={DbCommon.DbPath}");
        });


        return services;
    }
}
