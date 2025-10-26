using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Auth.Api.Operations;
using Modules.Shared.Attributes;

namespace Modules.Auth.Api.Endpoints;

internal static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/auth")
            .RequireAuthorization();

        api.MapAuthGets()
            .MapAuthPosts();

        return app;
    }

    private static RouteGroupBuilder MapAuthGets(this RouteGroupBuilder api)
    {
        api.MapGet("Ping", AuthOperations.Ping)
            .WithSummary("Ping Authorization service")
            .AllowAnonymous();

        api.MapGet("GetCurrentUser", AuthOperations.GetCurrentUserInfo)
            .WithSummary("Get information about current user")
            .AllowAnonymous();

        return api;
    }

    private static void MapAuthPosts(this RouteGroupBuilder api)
    {
        api.MapPost("Login", AuthOperations.Login)
            .WithSummary("Log in user")
            .AllowAnonymous();

        api.MapPost("ChangePassword", AuthOperations.ChangePassword)
            .WithSummary("Change user's password")
            .AddEndpointFilter<BasicTokenAuthorizationFilter>();
        
        api.MapPost("ChangeEmail", AuthOperations.ChangeEmail)
            .WithSummary("Change user's email address")
            .AddEndpointFilter<BasicTokenAuthorizationFilter>();

        api.MapPost("CanLogIn", AuthOperations.CanLogIn)
            .WithSummary("Check if user can be logged in");

        api.MapPost("Register", AuthOperations.Register)
            .WithSummary("Register new user")
            .AllowAnonymous();

        api.MapPost("Logout", AuthOperations.Logout)
            .WithSummary("Log user out");

        api.MapPost("ValidateToken", AuthOperations.ValidateToken)
            .WithSummary("Validate authorization token")
            .AllowAnonymous()
            .WithMetadata(new AllowWithoutTokenValidationAttribute());

        api.MapPost("GetUserToken", AuthOperations.GetUserToken)
            .WithSummary("Get user's authorization token");

        api.MapPost("RefreshToken", AuthOperations.RefreshToken)
            .WithSummary("Refresh user's authorization token");
    }
}