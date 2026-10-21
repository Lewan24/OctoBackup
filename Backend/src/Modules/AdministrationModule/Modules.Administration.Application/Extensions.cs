using Microsoft.Extensions.DependencyInjection;
using Modules.Administration.Application.Services;
using Modules.Administration.Shared.Interfaces;

namespace Modules.Administration.Application;

public static class Extensions
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<AdminService>();
        services.AddScoped<IAdminModuleApi, AdminModuleApi>();

        return services;
    }
}
