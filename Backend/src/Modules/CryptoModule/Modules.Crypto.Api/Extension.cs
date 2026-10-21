using Microsoft.Extensions.DependencyInjection;
using Modules.Crypto.Application;

namespace Modules.Crypto.Api;

public static class Extension
{
    public static IServiceCollection AddCryptoModule(this IServiceCollection services)
    {
        services.AddCryptoApplication();

        return services;
    }
}
