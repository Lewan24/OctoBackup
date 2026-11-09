using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Modules.Shared.Settings;
using Microsoft.Extensions.Logging;
using Modules.Auth.Application.Interfaces;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Auth.Shared.ActionsRequests;
using Modules.Auth.Shared.Entities;
using Modules.Auth.Shared.Static.Entities;

namespace Modules.Auth.Application.Services;

internal sealed class AuthService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    AppIdentityDbContext db,
    IApplicationSettings settings,
    ILogger<AuthService> logger)
    : IAuthService
{
    public async Task<IResult> Login(HttpContext context, LoginRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return RejectLogin(context, "missing_credentials");
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null) return RejectLogin(context, "account_not_found");
        if (user.IsBlocked) return RejectLogin(context, "account_blocked");
        if (!user.EmailConfirmed) return RejectLogin(context, "account_unconfirmed");
        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded) return RejectLogin(context, result.IsLockedOut ? "account_locked_out"
            : result.IsNotAllowed ? "sign_in_not_allowed" : "invalid_credentials");
        logger.LogInformation("User {UserId} signed in", user.Id);
        return Results.NoContent();
    }

    private IResult RejectLogin(HttpContext context, string reason)
    {
        // Account status is for deployment operators only. Keep browser failures
        // uniform and never log submitted emails, passwords or cookies.
        logger.LogWarning("Sign-in rejected: {Reason}. Trace {TraceId}", reason, context.TraceIdentifier);
        return Results.Json(new {
            code = "login_rejected",
            title = "Sign-in failed. Check your email and password, or ask an administrator to check account access.",
            traceId = context.TraceIdentifier
        }, statusCode: StatusCodes.Status401Unauthorized);
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
        if (result.Succeeded) await signInManager.SignOutAsync();
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

        if (!(await signInManager.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true)).Succeeded)
            return Results.BadRequest("Current password is incorrect.");
        await userManager.UpdateSecurityStampAsync(user);
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

    public async Task<IResult> Register(HttpContext context, RegisterRequest request)
    {
        var policy = settings.Current;
        var userName = context.User.Identity?.Name;

        var currentUser = userName is null
            ? null
            : await userManager.FindByNameAsync(userName);

        var isUserAdmin = currentUser is not null && await userManager.IsInRoleAsync(currentUser, AppRoles.Admin);

        if (!isUserAdmin && !policy.RegistrationEnabled)
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


        if (policy.AutoConfirmAccounts || isUserAdmin)
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

}
