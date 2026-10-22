namespace Modules.Backup.Core.Entities.Models;

public record EmailProviderConfigurationModel
{
    public EmailSettings ProviderSettings { get; init; } = new();
    public EmailProviderCredentials EmailSenderCredentials { get; init; } = null!;
    public List<string> EmailReceivers { get; set; } = [];
}

public record EmailProviderCredentials(
    string EmailSender,
    string EmailSenderDisplayName,
    string Password,
    string SmtpHost,
    int SmtpPort = 587);

public record EmailSettings(
    bool EnableEmailProvider = true,
    bool UseStartTls = true,
    bool UseSslInstead = false,
    bool SendEmailOnEachDbSuccessfulBackup = false,
    bool SendEmailOnEachDbFailureBackup = true,
    bool SendEmailWithStatisticsAfterBackups = true,
    bool SendEmailOnOtherFailures = true);
