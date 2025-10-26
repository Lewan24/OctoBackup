using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.Backup.Application.Services;
using Modules.Shared.Attributes;

namespace Modules.Backup.Api.Statistics;

internal static class StatisticsEndpoints
{
    public static WebApplication MapStatsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/statistics")
            .RequireAuthorization()
            .AddEndpointFilter<BasicTokenAuthorizationFilter>();

        api.MapGet("MyDashboard", StatisticsOperations.GetMyDashboard)
            .WithSummary("Get user statistics for Dashboard");

        api.MapGet("AdminDashboard", StatisticsOperations.GetAdminDashboard)
            .WithSummary("Get Admin statistics for Dashboard")
            .AddEndpointFilter<AdminTokenAuthorizationFilter>();

        return app;
    }
}

internal abstract record StatisticsOperations
{
    public static async Task<IResult> GetMyDashboard(
        HttpContext context,
        [FromServices] StatisticsService service)
    {
        var result = await service.GetMyDashboard(context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> GetAdminDashboard(
        HttpContext context,
        [FromServices] StatisticsService service)
    {
        var result = await service.GetAdminDashboard();

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }
}