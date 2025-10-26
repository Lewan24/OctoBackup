using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Auth.Application.Interfaces;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Auth.Shared.ActionsRequests;
using Modules.Auth.Shared.Entities;
using Modules.Auth.Shared.Interfaces;
using Modules.Auth.Shared.Static.Entities;

namespace Modules.Auth.Application.Services;

internal sealed class AuthService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    AppIdentityDbContext db,
    ITokenValidationService tokenValidationService,
    IConfiguration config,
    ILogger<AuthService> logger)
    : IAuthService
{
    private readonly AuthSettings _authSettings =
        config.GetSection("AuthSettings").Get<AuthSettings>() ?? new AuthSettings();

    public async Task<IResult> Login(HttpContext context, LoginRequest? request)
    {
        logger.LogInformation("Trying to log {UserName} in...", request?.Email);

        if (!await CanLogIn(context, request))
            return Results.BadRequest("Can't log in. User is blocked or email is not confirmed.");

        var user = await userManager.FindByEmailAsync(request!.Email);
        if (user is null)
            return Results.NotFound("This user does not exist.");

        await signInManager.SignInAsync(user, request.RememberMe);

        logger.LogInformation("User {UserName} successfully logged in. Generating token...", user.Email);
        var result = await GetUserToken(context, request);
        if (string.IsNullOrWhiteSpace(result.Token))
            await CreateNewUserToken(request.Email, _authSettings.DefaultTokenExpirationTimeInMinutes);

        return Results.Accepted("/auth/Login", request.Email);
    }

    public async Task<IResult> ChangePassword(HttpContext context, ChangePasswordRequest request)
    {
        logger.LogInformation("Changing password for {UserName}...", context.User.Identity?.Name);

        if (!IsValidChangePasswordRequest(request, out var error))
            return Results.BadRequest(error);

        var user = await userManager.FindByNameAsync(context.User.Identity?.Name!);
        if (user is null)
            return Results.BadRequest("Can't find user.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        return result.Succeeded
            ? Results.Ok()
            : Results.BadRequest("Error while changing password. Check your password and try again.");
    }
    
    public async Task<IResult> ChangeEmail(HttpContext context, ChangeEmailRequest request)
    {
        logger.LogInformation("Changing email address for {UserName} to new one: [{NewEmail}]...", context.User.Identity?.Name, request.NewEmail);

        if (string.IsNullOrWhiteSpace(request.NewEmail) || string.IsNullOrWhiteSpace(request.ConfirmNewEmail))
            return Results.BadRequest("Cant process empty email address.");
        
        if (request.NewEmail != request.ConfirmNewEmail)
            return Results.BadRequest("Emails do not match.");

        var user = await userManager.FindByNameAsync(context.User.Identity?.Name!);
        if (user is null)
            return Results.BadRequest("Can't find user.");

        var changeEmailToken = await userManager.GenerateChangeEmailTokenAsync(user, request.NewEmail);
        var result = await userManager.ChangeEmailAsync(user,  request.NewEmail, changeEmailToken);
        
        if (result.Succeeded)
        {
            await userManager.SetUserNameAsync(user, request.NewEmail);
            await Logout(context);
        }
        
        return result.Succeeded
            ? Results.Ok()
            : Results.BadRequest("Error while changing email address. Try again later.");
    }

    public async Task<bool> CanLogIn(HttpContext context, LoginRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            logger.LogDebug("Empty request or empty email/password.");
            return false;
        }

        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is { EmailConfirmed: true, IsBlocked: false })
            return await userManager.CheckPasswordAsync(user, request.Password);

        return false;
    }

    public async Task<IResult> Register(HttpContext context, RegisterRequest request)
    {
        var userName = context.User.Identity?.Name;

        var currentUser = userName is null
            ? null
            : await userManager.FindByNameAsync(userName);

        var isUserAdmin = currentUser is not null && await userManager.IsInRoleAsync(currentUser, AppRoles.Admin);

        if (!isUserAdmin && !_authSettings.EnableRegisterModule)
            return Results.BadRequest("Moduł rejestracji został wyłączony.");

        if (!IsValidRegisterRequest(request, out var error))
            return Results.BadRequest(error);

        var newUser = new AppUser { UserName = request.Email, Email = request.Email };
        var result = await userManager.CreateAsync(newUser, request.Password!);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors.First().Description);

        var createdUser = await userManager.FindByIdAsync(newUser.Id);
        if (createdUser is null)
            return Results.NotFound("Can't find created user. Try again");

        await userManager.AddToRoleAsync(createdUser, AppRoles.User);
        if (createdUser.Email!.Equals(_authSettings.MainAdminEmailAddress, StringComparison.OrdinalIgnoreCase))
            await userManager.AddToRoleAsync(createdUser, AppRoles.Admin);

        if (_authSettings.AutoConfirmAccount)
        {
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(newUser);
            await userManager.ConfirmEmailAsync(newUser, confirmationToken);
        }

        return Results.Created("/auth/Register", createdUser.Email);
    }

    public async Task<IResult> Logout(HttpContext context)
    {
        var currentUser = GetCurrentUserInfo(context);

        await signInManager.SignOutAsync();
        await CheckAndDeleteActiveUserTokens(currentUser.UserName);

        logger.LogInformation("Successfully logged user {UserName} off", currentUser.UserName);
        return Results.NoContent();
    }

    public CurrentUser GetCurrentUserInfo(HttpContext context)
    {
        var roles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();

        var claims = context.User.Claims
            .Where(c => c.Type != ClaimTypes.Role)
            .ToDictionary(c => c.Type, c => c.Value);

        claims[ClaimTypes.Role] = JsonSerializer.Serialize(roles);

        return new CurrentUser
        {
            IsAuthenticated = context.User.Identity?.IsAuthenticated ?? false,
            UserName = context.User.Identity?.Name,
            Claims = claims
        };
    }

    public Task<IResult> ValidateToken(TokenValidationRequest request)
    {
        var isValid = tokenValidationService.IsValid(request.Token, request.Email);

        return Task.FromResult(isValid.Match<IResult>(
            _ => Results.Ok(),
            _ => Results.BadRequest()
        ));
    }

    public async Task<TokenModelDto> GetUserToken(HttpContext context, LoginRequest request)
    {
        logger.LogInformation("Fetching Token for User {UserName} ...", request.Email);

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return new TokenModelDto();

        var token = db.UsersTokens.FirstOrDefault(t => t.Email == request.Email && t.ExpirationDate > DateTime.UtcNow);
        if (token == null)
            return new TokenModelDto();

        var isValid = tokenValidationService.IsValid(token.Token, request.Email);
        return await isValid.Match<Task<TokenModelDto>>(
            _ => Task.FromResult(new TokenModelDto
            {
                Token = token.Token,
                Email = token.Email,
                ExpirationDate = token.ExpirationDate
            }),
            async _ =>
            {
                db.UsersTokens.Remove(token);
                await db.SaveChangesAsync();
                return new TokenModelDto();
            });
    }

    public async Task<TokenModelDto> RefreshToken(HttpContext context, LoginRequest request)
    {
        logger.LogInformation("Refreshing token for {UserName} ...", request.Email);
        if (!await CanLogIn(context, request))
            return new TokenModelDto { ExpirationDate = DateTime.UtcNow.AddMinutes(-1) };

        return CheckIfAnyActiveUserTokenExist(request.Email) ??
               await CreateNewUserToken(request.Email, _authSettings.DefaultTokenExpirationTimeInMinutes);
    }

    // ----------------- Helpers (przeniesione z operacji) -----------------

    private static bool IsValidRegisterRequest(RegisterRequest request, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            error = "Wszystkie pola są wymagane";
        else if (request.Password != request.PasswordConfirm)
            error = "Hasła się nie zgadzają!";
        return string.IsNullOrEmpty(error);
    }

    private static bool IsValidChangePasswordRequest(ChangePasswordRequest request, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword) ||
            string.IsNullOrWhiteSpace(request.ConfirmNewPassword))
            error = "Nieprawidłowe dane";
        else if (request.NewPassword != request.ConfirmNewPassword) error = "Hasła się nie zgadzają";

        return string.IsNullOrEmpty(error);
    }

    private async Task CheckAndDeleteActiveUserTokens(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return;

        logger.LogDebug("Removing tokens from db for user {Email}...", userName);

        var tokens = db.UsersTokens.Where(t => t.Email == userName);
        if (tokens.Any())
        {
            db.UsersTokens.RemoveRange(tokens);
            await db.SaveChangesAsync();
        }
    }

    private TokenModelDto? CheckIfAnyActiveUserTokenExist(string userEmail)
    {
        logger.LogInformation("Searching DB for token for user {Email}...", userEmail);

        var token = db.UsersTokens.FirstOrDefault(ut => ut.Email == userEmail && ut.ExpirationDate > DateTime.UtcNow);

        if (token is null)
        {
            logger.LogDebug("Can't find any token. Returning null.");
            return null;
        }

        return new TokenModelDto
        {
            Email = token.Email,
            Token = token.Token,
            ExpirationDate = token.ExpirationDate
        };
    }

    private async Task<TokenModelDto> CreateNewUserToken(string userEmail, int expirationTimeInMinutes)
    {
        logger.LogDebug("Generating token for user {Email}...", userEmail);

        var random = new Random();
        var tokenValue = new string(Enumerable.Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 47)
            .Select(s => s[random.Next(s.Length)]).ToArray());

        var token = new TokenModel
        {
            Email = userEmail,
            ExpirationDate = DateTime.UtcNow.AddMinutes(expirationTimeInMinutes),
            Token = tokenValue
        };

        db.UsersTokens.Add(token);
        await db.SaveChangesAsync();

        return new TokenModelDto
        {
            Token = token.Token,
            Email = token.Email,
            ExpirationDate = token.ExpirationDate
        };
    }
}