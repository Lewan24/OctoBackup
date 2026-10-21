using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Core.Entities;
using Modules.Auth.Infrastructure.DbContexts;
using Modules.Auth.Shared.ActionsRequests;
using Modules.Auth.Shared.Dtos;
using Modules.Auth.Shared.Static.Entities;
using OneOf;
using OneOf.Types;

namespace Modules.Administration.Application.Services;

public class AdminService(
    AppIdentityDbContext context,
    UserManager<AppUser> userManager)
{
    public async Task<bool?> IsUserAdmin(string? identityName)
    {
        if (string.IsNullOrWhiteSpace(identityName))
            return null;

        var user = context.Users.SingleOrDefault(u => u.UserName == identityName);

        if (user is null)
            return null;

        var isUserAdmin = await userManager.IsInRoleAsync(user, AppRoles.Admin);

        return isUserAdmin;
    }

    public async Task<List<UserDto>> GetUsersList()
    {
        var users = await context.Users.AsNoTracking().ToListAsync();
        var roles = await (from ur in context.UserRoles join r in context.Roles on ur.RoleId equals r.Id
                           select new { ur.UserId, r.Name }).ToListAsync();
        return users.Select(u => new UserDto { Id = Guid.Parse(u.Id), Email = u.Email!, IsBlocked = u.IsBlocked,
            IsEmailConfirmed = u.EmailConfirmed, Roles = roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).ToList() }).ToList();
    }

    public async Task<OneOf<Success, string>> ToggleUserBlockade(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "User Id cannot be null or empty.";

        var user = context.Users.AsTracking().SingleOrDefault(u => u.Id == userId);

        if (user is null)
            return "Can't find specified user";

        var isAdmin = await IsUserAdmin(user.UserName);
        if (isAdmin is not null && isAdmin.Value)
            return "Can't change blockade for admin user";

        user.IsBlocked = !user.IsBlocked;
        await userManager.UpdateSecurityStampAsync(user);
        await context.SaveChangesAsync();

        return new Success();
    }

    public async Task<OneOf<Success, string>> ConfirmUser(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return "Can't find specified user.";
        if (user.EmailConfirmed) return new Success();
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded ? new Success() : "Could not confirm account.";
    }

    public async Task<OneOf<Success, string>> EditUser(EditUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Id))
            return "Invalid Request. Check data and try again.";

        var user = await userManager.FindByIdAsync(request.Id);

        if (user is null)
            return "Can't find specified user";

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password != request.ConfirmPassword) return "Passwords do not match.";
            foreach (var validator in userManager.PasswordValidators)
            {
                var validation = await validator.ValidateAsync(userManager, user, request.Password);
                if (!validation.Succeeded) return string.Join(" ", validation.Errors.Select(e => e.Description));
            }
        }
        var existing = await userManager.FindByEmailAsync(request.Email);
        if (existing is not null && existing.Id != user.Id) return "This email is already in use.";
        await using var transaction = await context.Database.BeginTransactionAsync();
        var emailResult = await userManager.SetEmailAsync(user, request.Email);
        if (!emailResult.Succeeded) return "Could not update email.";
        var nameResult = await userManager.SetUserNameAsync(user, request.Email);
        if (!nameResult.Succeeded) return "Could not update username.";
        if (!user.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            if (!(await userManager.ConfirmEmailAsync(user, token)).Succeeded) return "Could not confirm account.";
        }
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, request.Password);
            if (!result.Succeeded) return "Could not reset password.";
        }
        await userManager.UpdateSecurityStampAsync(user);
        await transaction.CommitAsync();
        return new Success();
    }
}
