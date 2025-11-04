using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modules.Shared.Attributes;

namespace Modules.BackupsTests.Api;

internal static class BackupsTestsEndpoints
{
    public static WebApplication MapBackupsTestsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/BackupsTests");
            // .RequireAuthorization()
            // .AddEndpointFilter<BasicTokenAuthorizationFilter>();

        api.MapGet("RunTest", BackupsTestsOperations.RunTest)
            .WithSummary("Run test dev container");

        return app;
    }
}

internal abstract record BackupsTestsOperations
{
    public static async Task<IResult> RunTest(
        HttpContext context,
        ILogger<BackupsTestsOperations> logger)
    {
        var container = new ContainerBuilder()
            .WithImage("testcontainers/helloworld:1.3.0")
            .WithPortBinding(8080, true)
            // Wait until the HTTP endpoint of the container is available.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080)))
            .Build();

        await container.StartAsync()
            .ConfigureAwait(false);

        using var httpClient = new HttpClient();

        // Construct the request URI by specifying the scheme, hostname, assigned random host port, and the endpoint "uuid".
        var requestUri = new UriBuilder(Uri.UriSchemeHttp, container.Hostname, container.GetMappedPublicPort(8080), "uuid").Uri;

        // Send an HTTP GET request to the specified URI and retrieve the response as a string.
        var guid = await httpClient.GetStringAsync(requestUri).ConfigureAwait(false);

        Debug.Assert(Guid.TryParse(guid, out _));
        logger.LogInformation("Success guid: {ContainerGuid}", guid);
        
        return TypedResults.Ok(guid);
    }
}