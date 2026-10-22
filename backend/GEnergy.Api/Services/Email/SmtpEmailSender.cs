using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
namespace GEnergy.Api.Services.Email;

public interface IEmailSender
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> configured) : IEmailSender
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var options = configured.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = new SmtpClient { Timeout = 30000 };
        // TLS is mandatory; certificate validation is never disabled.
        await client.ConnectAsync(options.Host, options.Port, options.Security == "SslOnConnect" ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, timeout.Token);
        if (!string.IsNullOrWhiteSpace(options.Username))
            await client.AuthenticateAsync(options.Username, options.Password, timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
