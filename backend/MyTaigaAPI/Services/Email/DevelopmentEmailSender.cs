using System.Text;

namespace MyTaigaAPI.Services.Email;

public class DevelopmentEmailSender : IEmailSender
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public DevelopmentEmailSender(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Bộ lưu email thử chỉ được dùng trong Development.");
        }

        var directory = _configuration["Email:DevelopmentDirectory"] ?? Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "dev-emails");

        Directory.CreateDirectory(directory);

        var fileName =
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.txt";

        var content = string.Join(
            Environment.NewLine,
            $"To: {recipient}",
            $"Subject: {subject}",
            $"CreatedAtUtc: {DateTime.UtcNow:O}",
            "",
            body);

        await File.WriteAllTextAsync(
            Path.Combine(directory, fileName),
            content,
            Encoding.UTF8,
            cancellationToken);
    }
}
