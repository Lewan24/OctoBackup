using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;
using Server.Api.Configuration;
using Xunit;

public sealed class ConfigurationReloadTests
{
    private static async Task Until(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!ready()) await Task.Delay(25, deadline.Token);
    }

    private static void Replace(string path, string json)
    {
        File.WriteAllText(path + ".new", json);
        File.Move(path + ".new", path, overwrite: true);
    }

    [Fact]
    public async Task LocalJson_ReloadsMappings_PreservesStartupSecretsAndPrecedence_AndRejectsIncompleteSaves()
    {
        var directory=Path.Combine(Path.GetTempPath(),"octobackup-reload-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
        var baseFile=Path.Combine(directory,"appsettings.json"); var localFile=Path.Combine(directory,DevelopmentSettings.FileName);
        File.WriteAllText(baseFile,"""{"CRYPTO_KEY":"base-key","Storage":{"DataDirectory":"original-data"},"RestoreTests":{"Enabled":false}}""");
        File.WriteAllText(localFile,"""{"CRYPTO_KEY":"local-key","RestoreTests":{"MySqlImages":{"8.3":"fixture/mysql:old"}}}""");
        using var configuration=new ConfigurationManager();
        configuration.AddJsonFile(baseFile).AddInMemoryCollection(new Dictionary<string,string?> { ["RestoreTests:PostgresImages:17"]="explicit/image:17" });
        DevelopmentSettings.Configure(configuration,directory,true);
        var log=ReloadableAppSettings.Configure(configuration,directory);
        var events=new ConcurrentQueue<string>(); log.Observe=events.Enqueue;
        Replace(localFile,"""{"CRYPTO_KEY":"replacement-never-used","Storage":{"DataDirectory":"different-data"},"RestoreTests":{"Enabled":true,"MySqlImages":{"8.3":"fixture/mysql:new"},"PostgresImages":{"17":"file/image:17"}}}""");
        await Until(()=>configuration["RestoreTests:MySqlImages:8.3"]=="fixture/mysql:new");
        Assert.Equal("local-key",configuration["CRYPTO_KEY"]); Assert.Equal("original-data",configuration["Storage:DataDirectory"]);
        Assert.Equal("explicit/image:17",configuration["RestoreTests:PostgresImages:17"]);
        Assert.True(configuration.GetValue<bool>("RestoreTests:Enabled")); Assert.Contains("startup",events);
        var profile=RestoreArchiveProfile.FromHeader(new BackupTest { DatabaseType=DatabaseType.MySql },"-- Server version 8.3.2\n",configuration);
        Assert.Equal("fixture/mysql:new",profile.Image);
        Replace(localFile,"{\"private-partial-secret");
        await Until(()=>events.Contains("invalid"));
        Assert.Equal("fixture/mysql:new",configuration["RestoreTests:MySqlImages:8.3"]);
        while(events.TryDequeue(out _)) { }
        File.Delete(localFile); await Until(()=>events.Contains("invalid"));
        Assert.Equal("local-key",configuration["CRYPTO_KEY"]); Assert.True(configuration.GetValue<bool>("RestoreTests:Enabled"));
        Replace(localFile,"""{"RestoreTests":{"Enabled":false,"MySqlImages":{"8.3":"fixture/mysql:final"}}}""");
        await Until(()=>configuration["RestoreTests:MySqlImages:8.3"]=="fixture/mysql:final");
        Assert.False(configuration.GetValue<bool>("RestoreTests:Enabled")); Assert.Equal("local-key",configuration["CRYPTO_KEY"]);
    }

    [Fact]
    public async Task BaseAndRuntimeOverrides_AreWatched_And_DefaultsReloadUntilAnAdminSnapshotIsSaved()
    {
        var directory=Path.Combine(Path.GetTempPath(),"octobackup-default-reload-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"appsettings.json"); var runtime=Path.Combine(directory,"deployment.json");
        File.WriteAllText(path,"""{"Backups":{"DefaultRetentionDays":4},"Security":{"AllowedDatabaseHosts":["first.example"]}}""");
        using var configuration=new ConfigurationManager(); configuration.AddJsonFile(path);
        configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["Configuration:ReloadFile"]=runtime });
        ReloadableAppSettings.Configure(configuration,directory);
        using var settings=new FileApplicationSettings(configuration,directory);
        Replace(path,"""{"Backups":{"DefaultRetentionDays":9},"Security":{"AllowedDatabaseHosts":["second.example"]}}""");
        await Until(()=>settings.Current.DefaultRetentionDays==9);
        Assert.Equal(new[]{"second.example"},settings.Current.AllowedDatabaseHosts);
        Replace(runtime,"""{"RestoreTests":{"MySqlImages":{"8.3":"fixture/external:8.3"}},"Backups":{"DefaultRetentionDays":12}}""");
        await Until(()=>settings.Current.DefaultRetentionDays==12);
        Assert.Equal("fixture/external:8.3",configuration["RestoreTests:MySqlImages:8.3"]);
        var saved=await settings.Save(settings.Current with { DefaultRetentionDays=15 },"fixture-admin"); Assert.NotNull(saved);
        var reloaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription=ChangeToken.OnChange(()=>((IConfiguration)configuration).GetReloadToken(),()=>reloaded.TrySetResult());
        Replace(runtime,"""{"RestoreTests":{"MySqlImages":{"8.3":"fixture/external:new"}},"Backups":{"DefaultRetentionDays":20}}""");
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Equal("fixture/external:new",configuration["RestoreTests:MySqlImages:8.3"]);
        Assert.Equal(15,settings.Current.DefaultRetentionDays); Assert.Equal(saved!.Revision,settings.Current.Revision);
    }
}
