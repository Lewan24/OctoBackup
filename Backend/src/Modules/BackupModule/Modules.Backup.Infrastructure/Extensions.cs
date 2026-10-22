using Microsoft.Extensions.DependencyInjection;
using Modules.Backup.Infrastructure.DbContexts;

namespace Modules.Backup.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
    {
        services.AddDbContext<BackupsDbContext>();

        return services;
    }
}
