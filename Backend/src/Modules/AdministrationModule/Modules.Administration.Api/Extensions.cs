using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modules.Administration.Api.Endpoints;
using Modules.Administration.Application;

namespace Modules.Administration.Api;

public static class Extensions
{
    public static IServiceCollection AddAdministrationModule(this IServiceCollection services)
    {
        services.AddApplicationLayer();

        return services;
    }

    public static WebApplication MapAdministrationModuleEndpoints(this WebApplication app)
    {
        app.MapAdministrationEndpoints();

        return app;
    }
}
