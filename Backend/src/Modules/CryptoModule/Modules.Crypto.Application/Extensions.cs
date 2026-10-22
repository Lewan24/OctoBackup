using Microsoft.Extensions.DependencyInjection;
using Modules.Crypto.Application.Services;
using Modules.Crypto.Shared.Interfaces;

namespace Modules.Crypto.Application;

public static class Extensions
{
    public static IServiceCollection AddCryptoApplication(this IServiceCollection services)
    {
        services.AddScoped<ICryptoService, CryptoService>();

        return services;
    }
}
