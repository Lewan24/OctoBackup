using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modules.Shared.Attributes;
using MySqlConnector;

namespace Modules.BackupsTests.Api;

internal static class BackupsTestsEndpoints
{
    public static WebApplication MapBackupsTestsEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/BackupsTests");
            // .RequireAuthorization()
            // .AddEndpointFilter<BasicTokenAuthorizationFilter>();

        api.MapPost("RunTest", BackupsTestsOperations.RunTest)
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
        //TODO: Implement accepting id of backup, get it from db, check file path, file name, unzip file etc
        // TODO: Implement additional status for backup test like running, failed, success etc
        var backupFolder = "/home/artur/.local/share/OctoBackup/Backups/localhost_3306/app"; // lokalny folder z dump.sql
        var dbName = "app";
        var password = "toor";

        logger.LogInformation("Budowanie kontenera");
        // 1️⃣ Tworzymy kontener MySQL z mapowaniem folderu backupu
        var container = new ContainerBuilder()
            .WithImage("mysql:8.0")
            .WithEnvironment("MYSQL_ROOT_PASSWORD", password)
            .WithEnvironment("MYSQL_DATABASE", dbName)
            .WithBindMount(backupFolder, "/backup")
            .WithPortBinding(3306, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready for connections"))
            .Build();

        logger.LogInformation("Startuje kontener");
        await container.StartAsync().ConfigureAwait(false);

        logger.LogInformation("Pobieram dane kontenera");
        var host = container.Hostname;
        var port = container.GetMappedPublicPort(3306);
        
        logger.LogInformation("Hostname: {Host}, Port: {Port}", host, port);
        
        logger.LogInformation("Czekam na możliwość podlaczenia sie do bazy");
        // 2️⃣ Czekamy aż MySQL wystartuje
        var connStr = $"Server={host};Port={port};User ID=root;Password={password};Database={dbName};SslMode=none;AllowPublicKeyRetrieval=True";
        await WaitForDatabaseAsync(connStr);

        logger.LogInformation("Przywracam backup");
        // 3️⃣ Przywrócenie backupu (załóżmy że dump.sql leży w /backup)
        await container.ExecAsync(["bash", "-c", $"mysql -uroot -p{password} {dbName} < /backup/2025.11.05.08.04.sql"]);

        // 4️⃣ Sprawdzenie czy są tabele inne niż systemowe
        await using var conn = new MySqlConnection(connStr);
        await conn.OpenAsync();

        logger.LogInformation("Sprawdzam czy zostaly jakies tabele stworzone");
        var cmd = new MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.tables WHERE table_schema = @db AND TABLE_NAME NOT LIKE 'sys_%';",
            conn);
        cmd.Parameters.AddWithValue("@db", dbName);

        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        logger.LogInformation("Znalezione tabele: {Tables}", string.Join(", ", tables));

        logger.LogInformation("Stopuje i usuwam testowy kontener");
        await container.StopAsync();
        await container.DisposeAsync();

        logger.LogInformation("Zwracam wynik");
        return TypedResults.Ok(new
        {
            Tables = tables,
            Success = tables.Count > 0
        });
    }
    
    private static async Task WaitForDatabaseAsync(string connStr)
    {
        var timeout = TimeSpan.FromSeconds(30);
        var sw = Stopwatch.StartNew();

        Exception exception = new();
        
        while (sw.Elapsed < timeout)
        {
            try
            {
                await using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();
                return;
            }
            catch (Exception ex)
            {
                exception = ex;
                await Task.Delay(1000);
            }
        }

        throw new Exception($"MySQL container did not start in time. Error: {exception.Message}", exception);
    }
}