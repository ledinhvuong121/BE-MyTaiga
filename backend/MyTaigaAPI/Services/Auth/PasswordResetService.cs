using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services.Email;
using Task = System.Threading.Tasks.Task;

namespace MyTaigaAPI.Services.Auth;

public class PasswordResetService
{
    private readonly ScrumbanDbContext _db;
    private readonly SystemConfigService _config;
    private readonly IEmailSender _emailSender;

    public PasswordResetService(
        ScrumbanDbContext db,
        SystemConfigService config,
        IEmailSender emailSender)
    {
        _db = db;
        _config = config;
        _emailSender = emailSender;
    }

    public async Task RequestAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var minutes = await _config.GetPositiveIntAsync(
            "RESET_TOKEN_EXPIRE_MINUTES",
            cancellationToken);

        var normalizedEmail = email.Trim();
        string recipient;
        string token;

        await using (var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            var user = await _db.Users
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK)
                    WHERE Email = {normalizedEmail}
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            // Không phát mã cho tài khoản không tồn tại hoặc bị khóa.
            if (user is null || user.Status != 1)
                return;

            token = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32));

            var now = DateTime.UtcNow;

            // Mã mới thay thế mọi mã chưa sử dụng trước đó.
            await _db.PasswordResetTokens
                .Where(x => x.UserId == user.UserId && !x.IsUsed)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.IsUsed, true),
                    cancellationToken);

            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.UserId,
                TokenHash = HashToken(token),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(minutes),
                IsUsed = false
            });

            _db.ActivityLogs.Add(new ActivityLog
            {
                UserId = user.UserId,
                Action = "PASSWORD_RESET_REQUESTED",
                EntityType = "USER",
                EntityId = user.UserId,
                Description = "Yêu cầu đặt lại mật khẩu.",
                CreatedAt = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            recipient = user.Email;
        }

        // Chưa có trang frontend khôi phục, nên email thử chứa mã
        // để kiểm thử API reset-password trực tiếp.
        var body =
            $"Bạn đã yêu cầu đặt lại mật khẩu MyTaiga.\n\n" +
            $"Mã đặt lại mật khẩu: {token}\n\n" +
            $"Mã có hiệu lực {minutes} phút và chỉ dùng một lần.\n" +
            "Nếu bạn không yêu cầu, hãy bỏ qua email này.";

        await _emailSender.SendAsync(
            recipient,
            "Đặt lại mật khẩu MyTaiga",
            body,
            cancellationToken);
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
    public async System.Threading.Tasks.Task<bool> ResetAsync(
    string token,
    string newPassword,
    CancellationToken cancellationToken = default)
{
    var minLength = await _config.GetPositiveIntAsync(
        "MIN_PASSWORD_LENGTH",
        cancellationToken);

    var requiredLength = Math.Max(8, minLength);

    if (string.IsNullOrEmpty(newPassword)
        || newPassword.Length < requiredLength
        || Encoding.UTF8.GetByteCount(newPassword) > 72)
    {
        throw new ArgumentException(
            $"Mật khẩu cần ít nhất {requiredLength} ký tự "
            + "và không vượt quá 72 byte UTF-8.");
    }

    var normalizedToken = token?.Trim();

    if (string.IsNullOrEmpty(normalizedToken)
        || normalizedToken.Length != 64
        || !normalizedToken.All(Uri.IsHexDigit))
    {
        return false;
    }

    var tokenHash = HashToken(normalizedToken.ToUpperInvariant());

    var candidate = await _db.PasswordResetTokens
        .AsNoTracking()
        .Where(x => x.TokenHash == tokenHash)
        .Select(x => new
        {
            x.TokenId,
            x.UserId
        })
        .SingleOrDefaultAsync(cancellationToken);

    if (candidate is null)
        return false;

    var passwordHash = BCrypt.Net.BCrypt.HashPassword(
        newPassword,
        workFactor: 12);

    await using var transaction =
        await _db.Database.BeginTransactionAsync(cancellationToken);

    // Khóa bản ghi người dùng để tránh xử lý đồng thời.
    var user = await _db.Users
        .FromSqlInterpolated($"""
            SELECT *
            FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK)
            WHERE UserId = {candidate.UserId}
            """)
        .SingleOrDefaultAsync(cancellationToken);

    if (user is null || user.Status != 1)
        return false;

    var resetToken = await _db.PasswordResetTokens
        .SingleOrDefaultAsync(
            x => x.TokenId == candidate.TokenId
                && x.UserId == user.UserId
                && x.TokenHash == tokenHash,
            cancellationToken);

    var now = DateTime.UtcNow;

    if (resetToken is null
        || resetToken.IsUsed
        || resetToken.ExpiresAt <= now)
    {
        return false;
    }

    user.PasswordHash = passwordHash;
    user.FailedLoginCount = 0;
    user.TokenVersion = checked(user.TokenVersion + 1);
    user.UpdatedAt = now;

    // Vô hiệu hóa tất cả mã đặt lại mật khẩu của người dùng.
    await _db.PasswordResetTokens
        .Where(x => x.UserId == user.UserId && !x.IsUsed)
        .ExecuteUpdateAsync(
            setters => setters.SetProperty(x => x.IsUsed, true),
            cancellationToken);

    _db.ActivityLogs.Add(new ActivityLog
    {
        UserId = user.UserId,
        Action = "PASSWORD_RESET_COMPLETED",
        EntityType = "USER",
        EntityId = user.UserId,
        Description = "Đặt lại mật khẩu và thu hồi tất cả phiên đăng nhập.",
        CreatedAt = now
    });

    await _db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return true;
}
}