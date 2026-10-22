using Modules.Backup.Core.Entities.Models;
using Newtonsoft.Json;

namespace Modules.Backup.Infrastructure.Configuration;

public static class ApplicationSettings
{
    public static Task<AppEmailConfigurationModel> Prepare(string? configurationJson)
    {
        try
        {
            if (configurationJson is null)
                throw new ArgumentNullException(nameof(configurationJson));

            return Task.FromResult(JsonConvert.DeserializeObject<AppEmailConfigurationModel>(configurationJson))!;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return Task.FromResult(new AppEmailConfigurationModel());
        }
    }
}
