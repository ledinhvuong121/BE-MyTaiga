using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MyTaigaAPI.Services.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        string Required(string name)
        {
            var value = _configuration[$"Email:Smtp:{name}"];

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Chưa cấu hình Email:Smtp:{name}.");
            }

            return value;
        }

        var host = Required("Host");
        var username = Required("Username");
        var password = Required("Password");
        var fromAddress = Required("FromAddress");

        var fromName =
            _configuration["Email:Smtp:FromName"] ?? "MyTaiga";

        if (!int.TryParse(
                _configuration["Email:Smtp:Port"],
                out var port)
            || port != 587)
        {
            throw new InvalidOperationException(
                "SMTP hiện yêu cầu cổng 587 với STARTTLS.");
        }

        var message = new MimeMessage();

        message.From.Add(new MailboxAddress(
            fromName,
            fromAddress));

        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new TextPart("plain")
        {
            Text = body
        };

        using var client = new SmtpClient
        {
            Timeout = 30000
        };

        await client.ConnectAsync(
            host,
            port,
            SecureSocketOptions.StartTls,
            cancellationToken);

        await client.AuthenticateAsync(
            username,
            password,
            cancellationToken);

        await client.SendAsync(
            message,
            cancellationToken);

        await client.DisconnectAsync(
            true,
            cancellationToken);
    }
}