using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Modules.BackupsTests.Api;

public static class Extensions
{
    public static IServiceCollection AddBackupsTestsModule(this IServiceCollection services)
    {
        return services;
    }

    public static WebApplication MapBackupsTestsModuleEndpoints(this WebApplication app)
    {
        app.MapBackupsTestsEndpoints();

        return app;
    }
}