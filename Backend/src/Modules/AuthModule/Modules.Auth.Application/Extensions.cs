using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Application.Interfaces;
using Modules.Auth.Application.Services;


namespace Modules.Auth.Application;

public static class Extensions
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();


        return services;
    }
}
