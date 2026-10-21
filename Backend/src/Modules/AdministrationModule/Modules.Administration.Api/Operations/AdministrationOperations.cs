using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.Administration.Application.Services;
using Modules.Administration.Shared.Interfaces;
using Modules.Auth.Shared.ActionsRequests;

namespace Modules.Administration.Api.Operations;

internal abstract class AdministrationOperations
{
    public static async Task<IResult> ConfirmUser([FromServices] AdminService service, [FromBody] string userId)
    {
        var result = await service.ConfirmUser(userId);
        return result.Match<IResult>(_ => TypedResults.Ok(), error => TypedResults.BadRequest(error));
    }
    public static async Task<IResult> IsUserAdmin(
        HttpContext ctx,
        [FromServices] AdminService api,
        [FromBody] string username)
    {
        var result = await api.IsUserAdmin(username);

        return result switch
        {
            null => TypedResults.NotFound("User does not exist"),
            _ => TypedResults.Ok(result)
        };
    }

    public static async Task<IResult> AmIAdmin(
        HttpContext context,
        [FromServices] IAdminModuleApi api)
    {
        return TypedResults.Ok(await api.AmIAdmin(context.User.Identity?.Name));
    }

    public static async Task<IResult> GetUsersList(
        HttpContext context,
        [FromServices] AdminService api)
    {
        return TypedResults.Ok(await api.GetUsersList());
    }

    public static async Task<IResult> ToggleUserBlockade(
        HttpContext context,
        [FromServices] AdminService api,
        [FromBody] string userId)
    {
        var result = await api.ToggleUserBlockade(userId);

        return result.Match<IResult>(
            _ => TypedResults.Ok(),
            error => TypedResults.BadRequest(error)
        );
    }

    public static async Task<IResult> EditUser(
        HttpContext context,
        [FromServices] AdminService api,
        [FromBody] EditUserRequest request)
    {
        var result = await api.EditUser(request);

        return result.Match<IResult>(
            _ => TypedResults.Ok(),
            error => TypedResults.BadRequest(error)
        );
    }
}
