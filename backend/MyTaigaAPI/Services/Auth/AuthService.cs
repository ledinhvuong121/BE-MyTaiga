using System.Text;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Services.Auth;

public class AuthService
{
    private readonly ScrumbanDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly SystemConfigService _config;

    public AuthService(
        ScrumbanDbContext db,
        JwtTokenService jwt,
        SystemConfigService config)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim();

        var maxFailed = await _config.GetPositiveIntAsync(
            "MAX_FAILED_LOGIN", cancellationToken);

        var expireMinutes = await _config.GetPositiveIntAsync(
            "JWT_EXPIRE_MINUTES", cancellationToken);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(cancellationToken);

        // Khóa dòng tài khoản trong giao dịch để tránh mất lượt đếm
        // khi có nhiều yêu cầu đăng nhập cùng lúc.
        var user = await _db.Users
            .FromSqlInterpolated($"""
                SELECT *
                FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK)
                WHERE Email = {email}
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Failure(401, "Tài khoản hoặc mật khẩu không chính xác.");
        }

        if (user.Status != 1)
        {
            return Failure(
                403, "Tài khoản đã bị khóa, vui lòng liên hệ quản trị viên.");
        }

        var role = await _db.UserRoles
            .Where(x => x.UserId == user.UserId
                && x.RoleScope == "SYSTEM"
                && x.Role.Scope == "SYSTEM")
            .Select(x => x.Role.RoleName)
            .SingleOrDefaultAsync(cancellationToken);

        if (role is not ("ADMIN" or "USER"))
        {
            return Failure(503, "Vai trò tài khoản chưa được cấu hình đúng.");
        }

        bool passwordMatches;

        try
        {
            // BCrypt chỉ xử lý tối đa 72 byte mật khẩu.
            passwordMatches =
                Encoding.UTF8.GetByteCount(request.Password) <= 72
                && BCrypt.Net.BCrypt.Verify(
                    request.Password, user.PasswordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return Failure(
                503, "Mật khẩu tài khoản chưa được thiết lập đúng.");
        }

        if (!passwordMatches)
        {
            if (user.FailedLoginCount < maxFailed)
                user.FailedLoginCount++;

            var locked = user.FailedLoginCount >= maxFailed;

            if (locked)
{
    user.Status = 0;
    user.TokenVersion = checked(user.TokenVersion + 1);
}

            user.UpdatedAt = DateTime.UtcNow;

            AddLog(
                user.UserId,
                locked ? "ACCOUNT_AUTO_LOCKED" : "LOGIN_FAILED",
                locked
                    ? "Khóa tài khoản do đăng nhập sai liên tiếp."
                    : "Đăng nhập thất bại.");

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return locked
                ? Failure(
                    403,
                    "Tài khoản đã bị khóa do đăng nhập sai quá số lần cho phép.")
                : Failure(
                    401,
                    $"Tài khoản hoặc mật khẩu không chính xác. " +
                    $"Còn {maxFailed - user.FailedLoginCount} lần thử.");
        }

        var response = _jwt.CreateToken(user, role, expireMinutes);

        user.FailedLoginCount = 0;
        user.UpdatedAt = DateTime.UtcNow;

        AddLog(user.UserId, "LOGIN_SUCCESS", "Đăng nhập thành công.");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LoginResult
        {
            Success = true,
            StatusCode = 200,
            Message = "Đăng nhập thành công.",
            Data = response
        };
    }
    public async Task<bool> LogoutAllAsync(
    long userId,
    int tokenVersion,
    CancellationToken cancellationToken = default)
{
    await using var transaction =
        await _db.Database.BeginTransactionAsync(cancellationToken);

    var now = DateTime.UtcNow;

    var affected = await _db.Users
        .Where(user => user.UserId == userId
            && user.Status == 1
            && user.TokenVersion == tokenVersion)
        .ExecuteUpdateAsync(
            setters => setters
                .SetProperty(
                    user => user.TokenVersion,
                    user => user.TokenVersion + 1)
                .SetProperty(user => user.UpdatedAt, (DateTime?)now),
            cancellationToken);

    if (affected != 1)
        return false;

    AddLog(
        userId,
        "LOGOUT_ALL",
        "Đăng xuất tất cả phiên của tài khoản.");

    await _db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return true;
}

    private void AddLog(long userId, string action, string description)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            Action = action,
            EntityType = "USER",
            EntityId = userId,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static LoginResult Failure(int statusCode, string message)
    {
        return new LoginResult
        {
            Success = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}