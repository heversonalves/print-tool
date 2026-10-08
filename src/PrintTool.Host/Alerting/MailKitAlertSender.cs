using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace PrintTool.Host.Alerting;

/// <summary>
/// Envia alertas por e-mail via SMTP (testado com o relay do Gmail). Usa MailKit em vez do
/// <c>System.Net.Mail.SmtpClient</c> embutido no .NET, que está obsoleto.
/// </summary>
public sealed class MailKitAlertSender : IAlertSender
{
    private readonly AlertingOptions _options;
    private readonly ILogger<MailKitAlertSender> _logger;

    public MailKitAlertSender(AlertingOptions options, ILogger<MailKitAlertSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(string subject, string body, CancellationToken cancellationToken)
    {
        SmtpSettings smtp = _options.Smtp;
        if (!smtp.IsConfigured)
        {
            _logger.LogWarning(
                "Alerta não enviado (SMTP não configurado em security/alerting.json): {Subject}", subject);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(smtp.SenderEmail));
            message.To.Add(MailboxAddress.Parse(smtp.RecipientEmail));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.StartTls, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(smtp.SenderEmail, smtp.SenderAppPassword, cancellationToken).ConfigureAwait(false);
            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Alerta enviado para {Recipient}: {Subject}", smtp.RecipientEmail, subject);
        }
        catch (Exception ex)
        {
            // Uma falha ao mandar e-mail (SMTP fora do ar, credencial errada, etc.) não pode
            // derrubar o monitoramento — só loga e segue; a próxima checagem tenta de novo.
            _logger.LogWarning(ex, "Falha ao enviar alerta por e-mail: {Subject}", subject);
        }
    }
}
