using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Modules.Backup.Application.Services;
using Modules.Backup.Shared.Dtos;
using Modules.Shared.Attributes;

namespace Modules.Backup.Api.Schedules;

internal static class SchedulesEndpoints
{
    public static WebApplication MapSchedulesEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/schedules")
            .RequireAuthorization()
            .AddEndpointFilter<RequestValidationFilter>()
            ;

        api.MapGet("GetMySchedules", SchedulesOperations.GetSchedules)
            .WithSummary("Get user's schedules");

        api.MapPost("CreateSchedule", SchedulesOperations.CreateSchedule)
            .WithSummary("Create new schedule");

        api.MapPost("EditSchedule", SchedulesOperations.EditSchedule)
            .WithSummary("Edit existing schedule");

        api.MapPost("DeleteSchedule", SchedulesOperations.DeleteSchedule)
            .WithSummary("Delete provided schedule");

        return app;
    }
}

internal abstract class SchedulesOperations
{
    public static async Task<IResult> GetSchedules(
        HttpContext context,
        [FromServices] SchedulesService service)
    {
        var result = await service.GetMySchedules(context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> CreateSchedule(
        HttpContext context,
        [FromServices] SchedulesService service,
        [FromBody] BackupsScheduleDto schedule)
    {
        var result = await service.CreateSchedule(schedule, context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> EditSchedule(
        HttpContext context,
        [FromServices] SchedulesService service,
        [FromBody] BackupsScheduleDto schedule)
    {
        var result = await service.EditSchedule(schedule, context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }

    public static async Task<IResult> DeleteSchedule(
        HttpContext context,
        [FromServices] SchedulesService service,
        [FromBody] Guid scheduleId)
    {
        var result = await service.DeleteSchedule(scheduleId, context.User.Identity?.Name);

        return result.Match<IResult>(
            TypedResults.Ok,
            TypedResults.BadRequest
        );
    }
}
