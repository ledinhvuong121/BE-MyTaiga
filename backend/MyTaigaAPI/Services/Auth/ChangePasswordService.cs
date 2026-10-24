using System.Text;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Services.Auth;

public class ChangePasswordService
{
    private readonly ScrumbanDbContext _db;
    private readonly SystemConfigService _config;

    public ChangePasswordService(
        ScrumbanDbContext db,
        SystemConfigService config)
    {
        _db = db;
        _config = config;
    }

    public async System.Threading.Tasks.Task<bool> ChangeAsync(
        long userId,
        int tokenVersion,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var minLength = await _config.GetPositiveIntAsync(
            "MIN_PASSWORD_LENGTH",
            cancellationToken);

        var requiredLength = Math.Max(8, minLength);

        if (string.IsNullOrEmpty(currentPassword)
            || Encoding.UTF8.GetByteCount(currentPassword) > 72)
        {
            throw new ArgumentException(
                "Mật khẩu hiện tại không hợp lệ.");
        }

        if (string.IsNullOrEmpty(newPassword)
            || newPassword.Length < requiredLength
            || Encoding.UTF8.GetByteCount(newPassword) > 72)
        {
            throw new ArgumentException(
                $"Mật khẩu mới cần ít nhất {requiredLength} ký tự "
                + "và không vượt quá 72 byte UTF-8.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = await _db.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK)
                WHERE UserId = {userId}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        // Kiểm tra lại trạng thái và phiên sau khi khóa bản ghi.
        if (user is null
            || user.Status != 1
            || user.TokenVersion != tokenVersion)
        {
            return false;
        }

        if (!BCrypt.Net.BCrypt.Verify(
                currentPassword,
                user.PasswordHash))
        {
            throw new ArgumentException(
                "Mật khẩu hiện tại không đúng.");
        }

        if (BCrypt.Net.BCrypt.Verify(
                newPassword,
                user.PasswordHash))
        {
            throw new ArgumentException(
                "Mật khẩu mới phải khác mật khẩu hiện tại.");
        }

        var now = DateTime.UtcNow;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(
            newPassword,
            workFactor: 12);

        user.TokenVersion = checked(user.TokenVersion + 1);
        user.FailedLoginCount = 0;
        user.UpdatedAt = now;

        // Thu hồi cả các mã quên mật khẩu chưa sử dụng.
        await _db.PasswordResetTokens
            .Where(x => x.UserId == userId && !x.IsUsed)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IsUsed, true),
                cancellationToken);

        _db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            Action = "PASSWORD_CHANGED",
            EntityType = "USER",
            EntityId = userId,
            Description = "Đổi mật khẩu và thu hồi tất cả phiên đăng nhập.",
            CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}