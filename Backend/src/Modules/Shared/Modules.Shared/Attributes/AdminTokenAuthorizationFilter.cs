using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Administration.Shared.Interfaces;
namespace Modules.Shared.Attributes;
public class AdminTokenAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var api = context.HttpContext.RequestServices.GetRequiredService<IAdminModuleApi>();
        if (!await api.AmIAdmin(context.HttpContext.User.Identity?.Name)) return Results.Forbid();
        return await next(context);
    }
}
