using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StoneFlow.Access.Application.Abstractions;
using StoneFlow.Access.Infrastructure.Options;

namespace StoneFlow.Access.Infrastructure.Email;

/// <summary>Wysyłka przez SMTP (MailKit). Błąd transportu jest logowany tylko jako typ, bez treści.</summary>
internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var smtp = options.Value.Smtp;
        var host = smtp.Host ?? throw new InvalidOperationException("Brak serwera SMTP w konfiguracji.");
        var fromAddress = smtp.FromAddress ?? throw new InvalidOperationException("Brak adresu nadawcy w konfiguracji.");
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(
                host,
                smtp.Port,
                smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                cancellationToken);
            if (!string.IsNullOrEmpty(smtp.UserName) && smtp.Password is { } password)
            {
                await client.AuthenticateAsync(smtp.UserName, password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return EmailDeliveryResult.Sent;
        }
        catch (Exception exception) when (exception is SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException
            or MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException
            or SslHandshakeException or IOException or SocketException or TimeoutException)
        {
            LogDeliveryFailed(exception.GetType().Name);
            return EmailDeliveryResult.Failed;
        }
    }

    [LoggerMessage(EventId = 6050, Level = LogLevel.Error, Message = "Nie udało się wysłać wiadomości e-mail: {ErrorType}.")]
    private partial void LogDeliveryFailed(string errorType);
}

/// <summary>Tylko Development: treść wiadomości (z linkiem) trafia do logu zamiast do skrzynki.</summary>
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task<EmailDeliveryResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogMessage(message.Subject, message.TextBody);
        return Task.FromResult(EmailDeliveryResult.Sent);
    }

    [LoggerMessage(EventId = 6051, Level = LogLevel.Warning, Message = "[Development] Wiadomość e-mail niewysłana, treść: {Subject}\n{Body}")]
    private partial void LogMessage(string subject, string body);
}
