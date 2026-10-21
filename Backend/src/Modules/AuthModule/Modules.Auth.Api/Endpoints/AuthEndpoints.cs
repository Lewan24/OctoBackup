using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Modules.Auth.Api.Operations;
using Modules.Shared.Attributes;

namespace Modules.Auth.Api.Endpoints;

internal static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/auth")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>();

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
        api.MapPost("Login", AuthOperations.Login).RequireRateLimiting("auth")
            .WithSummary("Log in user")
            .AllowAnonymous();

        api.MapPost("ChangePassword", AuthOperations.ChangePassword).RequireRateLimiting("auth")
            .WithSummary("Change user's password")
            ;

        api.MapPost("ChangeEmail", AuthOperations.ChangeEmail).RequireRateLimiting("auth")
            .WithSummary("Change user's email address")
            ;

        api.MapPost("Register", AuthOperations.Register).RequireRateLimiting("auth")
            .WithSummary("Register new user")
            .AllowAnonymous();

        api.MapPost("Logout", AuthOperations.Logout)
            .WithSummary("Log user out");

    }
}
