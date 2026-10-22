using Modules.Backup.Core.Entities.Models;

namespace Modules.Backup.Application.Interfaces;

public interface IEmailProviderService
{
    Task PrepareAndSendEmail(MailModel mailRequest);
    Task<EmailSettings> GetEmailSettings();
}
