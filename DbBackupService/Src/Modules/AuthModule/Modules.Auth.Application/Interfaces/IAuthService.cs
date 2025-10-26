using Microsoft.AspNetCore.Http;
using Modules.Auth.Shared.ActionsRequests;
using Modules.Auth.Shared.Entities;

namespace Modules.Auth.Application.Interfaces;

public interface IAuthService
{
    Task<IResult> Login(HttpContext context, LoginRequest? request);
    Task<IResult> ChangePassword(HttpContext context, ChangePasswordRequest request);
    Task<IResult> ChangeEmail(HttpContext context, ChangeEmailRequest request);
    Task<bool> CanLogIn(HttpContext context, LoginRequest? request);
    Task<IResult> Register(HttpContext context, RegisterRequest request);
    Task<IResult> Logout(HttpContext context);
    CurrentUser GetCurrentUserInfo(HttpContext context);
    Task<IResult> ValidateToken(TokenValidationRequest request);
    Task<TokenModelDto> GetUserToken(HttpContext context, LoginRequest request);
    Task<TokenModelDto> RefreshToken(HttpContext context, LoginRequest request);
}