using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Entities;
using MyTaigaAPI.Services;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly ScrumbanDbContext _db;
    private readonly IConfiguration _configuration;

    public UsersController(ScrumbanDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
                User.FindFirst("sub")?.Value, out var userId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Token không hợp lệ."
            });
        }

        var profile = await _db.Users
            .AsNoTracking()
            .Where(user => user.UserId == userId && user.Status == 1)
            .Select(user => new
            {
                user.UserId,
                user.FullName,
                user.Email,
                user.PhoneNumber,
                user.Avatar,
                systemRole = user.UserRole != null
                    && user.UserRole.RoleScope == "SYSTEM"
                    && user.UserRole.Role.Scope == "SYSTEM"
                        ? user.UserRole.Role.RoleName
                        : null,
                user.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Tài khoản không tồn tại hoặc đã bị khóa."
            });
        }

        if (profile.systemRole is not ("ADMIN" or "USER"))
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Vai trò tài khoản chưa được cấu hình đúng."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Lấy hồ sơ cá nhân thành công.",
            data = new
{
    profile.UserId,
    profile.FullName,
    profile.Email,
    profile.PhoneNumber,
    profile.Avatar,
    profile.systemRole,
    createdAt = DateTime.SpecifyKind(
        profile.CreatedAt, DateTimeKind.Utc)
}
        });
    }
    [HttpPut("me")]
public async Task<IActionResult> UpdateMe(
    [FromBody] UpdateProfileRequest request,
    CancellationToken cancellationToken)
{
    if (!long.TryParse(
            User.FindFirst("sub")?.Value, out var userId))
    {
        return Unauthorized(new
        {
            success = false,
            message = "Token không hợp lệ."
        });
    }

    var fullName = request.FullName.Trim();
    var email = request.Email.Trim();
    var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
        ? null
        : request.PhoneNumber.Trim();

    if (fullName.Length == 0 || email.Length == 0)
    {
        return BadRequest(new
        {
            success = false,
            message = "Họ tên và email không được để trống."
        });
    }

    var user = await _db.Users.SingleOrDefaultAsync(
        x => x.UserId == userId && x.Status == 1,
        cancellationToken);

    if (user is null)
    {
        return Unauthorized(new
        {
            success = false,
            message = "Tài khoản không tồn tại hoặc đã bị khóa."
        });
    }

    var emailExists = await _db.Users.AnyAsync(
        x => x.Email == email && x.UserId != userId,
        cancellationToken);

    if (emailExists)
    {
        return Conflict(new
        {
            success = false,
            message = "Email đã được tài khoản khác sử dụng."
        });
    }

    user.FullName = fullName;
    user.Email = email;
    user.PhoneNumber = phoneNumber;
    user.UpdatedAt = DateTime.UtcNow;

    _db.ActivityLogs.Add(new ActivityLog
    {
        UserId = userId,
        Action = "PROFILE_UPDATED",
        EntityType = "USER",
        EntityId = userId,
        Description = "Cập nhật hồ sơ cá nhân.",
        CreatedAt = DateTime.UtcNow
    });

    try
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException exception)
        when (exception.InnerException
            is Microsoft.Data.SqlClient.SqlException sqlException
            && (sqlException.Number == 2601
                || sqlException.Number == 2627))
    {
        return Conflict(new
        {
            success = false,
            message = "Dữ liệu bị trùng. Vui lòng kiểm tra lại email."
        });
    }

    return Ok(new
    {
        success = true,
        message = "Cập nhật hồ sơ thành công.",
        data = new
        {
            user.UserId,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            updatedAt = DateTime.SpecifyKind(
                user.UpdatedAt.Value, DateTimeKind.Utc)
        }
    });
}
[HttpPut("me/avatar")]
[Consumes("multipart/form-data")]
[RequestSizeLimit(6 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
public async Task<IActionResult> UpdateAvatar(
    [FromForm] UpdateAvatarRequest request,
    [FromServices] AvatarImageService imageService,
    [FromServices] CloudinaryImages cloud,
    [FromServices] IWebHostEnvironment environment,
    CancellationToken cancellationToken)
{
    if (!long.TryParse(
            User.FindFirst("sub")?.Value, out var userId))
    {
        return Unauthorized();
    }

    var user = await _db.Users.SingleOrDefaultAsync(
        x => x.UserId == userId && x.Status == 1,
        cancellationToken);

    if (user is null)
        return Unauthorized();

    if (request.File.Length > AvatarImageService.MaxFileBytes)
    {
        return StatusCode(413, new
        {
            success = false,
            message = "Ảnh đại diện tối đa 5 MB."
        });
    }

    byte[] imageBytes;

    try
    {
        imageBytes = await imageService.ConvertToPngAsync(
            request.File, cancellationToken);
    }
    catch (ArgumentException exception)
    {
        return BadRequest(new
        {
            success = false,
            message = exception.Message
        });
    }

    await using var input=new MemoryStream(imageBytes);
    var avatarUrl=await cloud.UploadAsync(input,"avatar.png",false,cancellationToken);
    var old=user.Avatar;
    try
    {
        user.Avatar=avatarUrl;user.UpdatedAt=DateTime.UtcNow;
        _db.ActivityLogs.Add(new ActivityLog{UserId=userId,Action="AVATAR_UPDATED",EntityType="USER",EntityId=userId,Description="Cập nhật avatar Cloudinary.",CreatedAt=DateTime.UtcNow});
        await _db.SaveChangesAsync(cancellationToken);
    }
    catch{await cloud.DeleteAsync(avatarUrl);throw;}
    if(old is not null&&cloud.IsCloud(old))await cloud.DeleteAsync(old);

    return Ok(new
    {
        success = true,
        message = "Cập nhật ảnh đại diện thành công.",
        data = new { avatar = avatarUrl }
    });
}
[HttpGet("me/avatar/{fileName}")]
public async Task<IActionResult> GetAvatar(
    string fileName,
    [FromServices] IWebHostEnvironment environment,
    CancellationToken cancellationToken)
{
    if (!long.TryParse(
            User.FindFirst("sub")?.Value, out var userId))
    {
        return Unauthorized();
    }

    // Chỉ nhận tên file do API upload tạo.
    if (!fileName.EndsWith(".png", StringComparison.Ordinal)
        || !Guid.TryParseExact(
            Path.GetFileNameWithoutExtension(fileName),
            "N",
            out var fileId)
        || fileName != $"{fileId:N}.png")
    {
        return NotFound();
    }

    var expectedUrl = $"/api/v1/users/me/avatar/{fileName}";

    var isCurrentAvatar = await _db.Users
        .AsNoTracking()
        .AnyAsync(
            user => user.UserId == userId
                && user.Status == 1
                && user.Avatar == expectedUrl,
            cancellationToken);

    if (!isCurrentAvatar)
        return NotFound();

    var root = _configuration["Storage:AvatarsRoot"] ?? Path.Combine(environment.ContentRootPath,"App_Data","avatars");
    var filePath = Path.Combine(root,fileName);

    if (!System.IO.File.Exists(filePath))
        return NotFound();

    Response.Headers.CacheControl = "no-store";
    Response.Headers["X-Content-Type-Options"] = "nosniff";

    return PhysicalFile(filePath, "image/png");
}
}
