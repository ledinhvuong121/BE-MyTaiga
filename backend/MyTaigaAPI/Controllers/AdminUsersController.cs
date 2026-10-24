using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Data;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/v1/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly ScrumbanDbContext _db;

    public AdminUsersController(ScrumbanDbContext db)
    {
        _db = db;
    }

    [HttpPatch("{userId:long}/lock")]
    public Task<IActionResult> Lock(
        long userId,
        CancellationToken cancellationToken)
    {
        return SetStatusAsync(userId, 0, cancellationToken);
    }

    [HttpPatch("{userId:long}/unlock")]
    public Task<IActionResult> Unlock(
        long userId,
        CancellationToken cancellationToken)
    {
        return SetStatusAsync(userId, 1, cancellationToken);
    }

    private async Task<IActionResult> SetStatusAsync(
        long userId,
        byte status,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
                User.FindFirst("sub")?.Value, out var adminId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Token không hợp lệ."
            });
        }

        if (userId <= 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã tài khoản phải lớn hơn 0."
            });
        }

        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                EXEC dbo.sp_SetUserStatus
                    @AdminId = {adminId},
                    @UserId = {userId},
                    @Status = {status}
                """,
                cancellationToken);
        }
        catch (SqlException exception)
            when (exception.Number is 50080 or 50081 or 50082 or 50083)
        {
            var httpStatus = exception.Number switch
            {
                50080 => StatusCodes.Status403Forbidden,
                50081 => StatusCodes.Status409Conflict,
                50082 => StatusCodes.Status400BadRequest,
                50083 => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            };

            return StatusCode(httpStatus, new
            {
                success = false,
                message = exception.Message
            });
        }

        return Ok(new
        {
            success = true,
            message = status == 0
                ? "Khóa tài khoản và thu hồi token thành công."
                : "Mở khóa tài khoản thành công.",
            data = new
            {
                userId,
                status
            }
        });
    }
}