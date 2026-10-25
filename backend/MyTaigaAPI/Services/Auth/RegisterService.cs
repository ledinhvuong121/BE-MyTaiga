using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Services.Auth;

public class RegisterService
{
    private readonly ScrumbanDbContext _db;
    private readonly SystemConfigService _config;

    public RegisterService(
        ScrumbanDbContext db,
        SystemConfigService config)
    {
        _db = db;
        _config = config;
    }

    public async System.Threading.Tasks.Task<(
        int StatusCode,
        string Message,
        long? UserId)> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (400, "Họ tên không được để trống.", null);
        }

        var minLength = await _config.GetPositiveIntAsync(
            "MIN_PASSWORD_LENGTH",
            cancellationToken);

        var requiredLength = Math.Max(8, minLength);

        if (request.Password.Length < requiredLength
            || Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return (
                400,
                $"Mật khẩu cần ít nhất {requiredLength} ký tự "
                    + "và không vượt quá 72 byte UTF-8.",
                null);
        }

        if (request.Password != request.ConfirmPassword)
        {
            return (400, "Hai mật khẩu không khớp.", null);
        }

        var emailExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Email == email,
                cancellationToken);

        if (emailExists)
        {
            return (409, "Email đã được sử dụng.", null);
        }

        var userRole = await _db.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                role => role.RoleName == "USER"
                    && role.Scope == "SYSTEM",
                cancellationToken);

        if (userRole is null)
        {
            return (
                503,
                "Hệ thống chưa cấu hình vai trò USER.",
                null);
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password,
            workFactor: 12);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                cancellationToken);

        var now = DateTime.UtcNow;

        var newUser = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = passwordHash,
            Status = 1,
            FailedLoginCount = 0,
            TokenVersion = 0,
            CreatedAt = now
        };

        // Chỉ cấp quyền USER, không lấy vai trò từ request.
        newUser.UserRole = new UserRole
        {
            RoleId = userRole.RoleId,
            RoleScope = "SYSTEM",
            User = newUser
        };

        _db.Users.Add(newUser);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            _db.ActivityLogs.Add(new ActivityLog
            {
                UserId = newUser.UserId,
                Action = "USER_REGISTERED",
                EntityType = "USER",
                EntityId = newUser.UserId,
                Description = "Đăng ký tài khoản người dùng.",
                CreatedAt = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException sqlException
                && (sqlException.Number == 2601
                    || sqlException.Number == 2627))
        {
            await transaction.RollbackAsync(cancellationToken);

            // Xác định có phải email trùng do hai yêu cầu đồng thời.
            var duplicateEmail = await _db.Users
                .AsNoTracking()
                .AnyAsync(
                    user => user.Email == email,
                    cancellationToken);

            if (duplicateEmail)
            {
                return (409, "Email đã được sử dụng.", null);
            }

            throw;
        }

        return (
            201,
            "Đăng ký tài khoản thành công. Vui lòng đăng nhập.",
            newUser.UserId);
    }
}