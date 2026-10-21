using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modules.Administration.Api.Operations;
using Modules.Shared.Attributes;

namespace Modules.Administration.Api.Endpoints;

internal static class AdministrationEndpoints
{
    public static WebApplication MapAdministrationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/administration")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>()
            .AddEndpointFilter<AdminTokenAuthorizationFilter>();

        app.MapGet("/api/administration/AmIAdmin", AdministrationOperations.AmIAdmin)
            .WithSummary("Check if the user is admin")
            .RequireAuthorization();

        api.MapPost("IsUserAdmin", AdministrationOperations.IsUserAdmin)
            .WithSummary("Check if the specified user is admin");

        api.MapGet("GetUsers", AdministrationOperations.GetUsersList)
            .WithSummary("Get all users");

        api.MapPost("ToggleUserBlockade", AdministrationOperations.ToggleUserBlockade)
            .WithSummary("Toggle user blockade");

        api.MapPost("EditUser", AdministrationOperations.EditUser)
            .WithSummary("Edit existing user");
        api.MapPost("ConfirmUser", AdministrationOperations.ConfirmUser)
            .WithSummary("Confirm an operator account");

        return app;
    }
}
