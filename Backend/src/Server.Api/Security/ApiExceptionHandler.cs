using Microsoft.AspNetCore.Diagnostics;

namespace Server.Api.Security;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        logger.LogError(exception, "Request failed. Trace {TraceId}", context.TraceIdentifier);
        var status = exception is ArgumentException or FormatException ? 400 : 500;
        await Results.Problem(statusCode: status, title: status == 400 ? "Invalid request." : "The operation failed. Check server logs with the trace ID.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
