namespace Modules.Backup.Core.Entities.Models;

public record AppEmailConfigurationModel
{
    public ApplicationConfigurationModel AppConfiguration { get; init; } = new();
    public EmailProviderConfigurationModel EmailProviderConfiguration { get; init; } = new();
}
