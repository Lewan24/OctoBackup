using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Modules.Auth.Application.Interfaces;
using Modules.Auth.Shared.ActionsRequests;
using Modules.Auth.Shared.Entities;

namespace Modules.Auth.Api.Operations;

internal abstract record AuthOperations
{
    public static IResult Ping(HttpContext context, ILogger<AuthOperations> logger)
    {
        logger.LogInformation("Ping from {UserEmail}", context.User.Identity?.Name);
        return TypedResults.Ok("pong");
    }

    public static Task<IResult> Login(
        HttpContext context,
        [FromBody] LoginRequest? request,
        [FromServices] IAuthService authService)
    {
        return authService.Login(context, request);
    }

    public static Task<IResult> ChangePassword(
        HttpContext context,
        [FromBody] ChangePasswordRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.ChangePassword(context, request);
    }
    
    public static Task<IResult> ChangeEmail(
        HttpContext context,
        [FromBody] ChangeEmailRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.ChangeEmail(context, request);
    }

    public static Task<bool> CanLogIn(
        HttpContext context,
        [FromBody] LoginRequest? request,
        [FromServices] IAuthService authService)
    {
        return authService.CanLogIn(context, request);
    }

    public static Task<IResult> Register(
        HttpContext context,
        [FromBody] RegisterRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.Register(context, request);
    }

    public static Task<IResult> Logout(
        HttpContext context,
        [FromServices] IAuthService authService)
    {
        return authService.Logout(context);
    }

    public static CurrentUser GetCurrentUserInfo(
        HttpContext context,
        [FromServices] IAuthService authService)
    {
        return authService.GetCurrentUserInfo(context);
    }

    public static Task<IResult> ValidateToken(
        [FromBody] TokenValidationRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.ValidateToken(request);
    }

    public static Task<TokenModelDto> GetUserToken(
        HttpContext context,
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.GetUserToken(context, request);
    }

    public static Task<TokenModelDto> RefreshToken(
        HttpContext context,
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService)
    {
        return authService.RefreshToken(context, request);
    }
}