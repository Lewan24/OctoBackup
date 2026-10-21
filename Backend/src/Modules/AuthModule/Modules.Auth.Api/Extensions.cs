using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Api.Endpoints;
using Modules.Auth.Application;
using Modules.Auth.Infrastructure;

namespace Modules.Auth.Api;

public static class Extensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddInfrastructureLayer();
        services.AddApplicationLayer();

        return services;
    }

    public static WebApplication MapAuthModuleEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();

        return app;
    }
}
