using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Modules.Backup.Application.Interfaces;
using Modules.Backup.Core.Entities.Models;
using NLog;

namespace Modules.Backup.Application.Services;

public class EmailProviderService(EmailProviderConfigurationModel configuration, Logger logger)
    : IEmailProviderService
{
    private readonly Logger _logger = logger.Factory.GetLogger(nameof(EmailProviderService));
    private readonly SmtpClient _smtpClient = new();

    public async Task PrepareAndSendEmail(MailModel mailRequest)
    {
        if (!configuration.ProviderSettings.EnableEmailProvider)
        {
            _logger.Info("Can't send email. EmailProvider disabled in settings");
            return;
        }

        _logger.Info("Preparing email provider and its components...");

        try
        {
            var mail = PrepareMessage(mailRequest);
            if (mail is null)
                throw new ArgumentNullException(nameof(mail), "Can't prepare message to send");

            await SendEmail(mail);
        }
        catch (ArgumentNullException e)
        {
            _logger.Warn(e, "Error thrown while preparing mail message.");
        }
        catch (Exception e)
        {
            _logger.Warn(e);
        }
        finally
        {
            _logger.Info("Email request had been handled.");
        }
    }

    public Task<EmailSettings> GetEmailSettings()
    {
        return Task.FromResult(configuration.ProviderSettings);
    }

    private MimeMessage? PrepareMessage(MailModel request)
    {
        _logger.Info("Preparing mail message...");
        var mail = new MimeMessage();

        mail.Subject = request.Subject;
        mail.Body = new TextPart("html")
        {
            Text = request.Body
        };

        mail.From.Add(new MailboxAddress(configuration.EmailSenderCredentials.EmailSenderDisplayName,
            configuration.EmailSenderCredentials.EmailSender));

        if (!configuration.EmailReceivers.Any())
        {
            _logger.Warn("Receivers list can't be empty");
            return null;
        }

        foreach (var receiver in configuration.EmailReceivers)
            mail.To.Add(new MailboxAddress(receiver, receiver));

        return mail;
    }

    private async Task SendEmail(MimeMessage mail)
    {
        try
        {
            _logger.Info("Connecting to mail server...");
            if (configuration.ProviderSettings.UseStartTls)
                await _smtpClient.ConnectAsync(configuration.EmailSenderCredentials.SmtpHost,
                    configuration.EmailSenderCredentials.SmtpPort, SecureSocketOptions.StartTls);
            else if (configuration.ProviderSettings.UseSslInstead)
                await _smtpClient.ConnectAsync(configuration.EmailSenderCredentials.SmtpHost,
                    configuration.EmailSenderCredentials.SmtpPort, configuration.ProviderSettings.UseSslInstead);
            else
                await _smtpClient.ConnectAsync(configuration.EmailSenderCredentials.SmtpHost,
                    configuration.EmailSenderCredentials.SmtpPort);

            _logger.Info("Authentication...");
            await _smtpClient.AuthenticateAsync(configuration.EmailSenderCredentials.EmailSender,
                configuration.EmailSenderCredentials.Password);

            _logger.Info("Sending {EmailSubject}...", mail.Subject);
            await _smtpClient.SendAsync(mail);

            _logger.Info("Stopping Email Provider and closing connection...");
            await _smtpClient.DisconnectAsync(true);
        }
        catch (Exception e)
        {
            _logger.Warn(e, "Error thrown on sending email: {EmailSubject}", mail.Subject);
            throw;
        }
    }
}
