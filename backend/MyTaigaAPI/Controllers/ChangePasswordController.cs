using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Services.Auth;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth")]
public class ChangePasswordController : ControllerBase
{
    private readonly ChangePasswordService _service;

    public ChangePasswordController(ChangePasswordService service)
    {
        _service = service;
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        var versionClaim = User.FindFirst("token_version")?.Value;

        if (!long.TryParse(userIdClaim, out var userId)
            || !int.TryParse(versionClaim, out var tokenVersion)
            || tokenVersion < 0)
        {
            return Unauthorized();
        }

        bool succeeded;

        try
        {
            succeeded = await _service.ChangeAsync(
                userId,
                tokenVersion,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message,
                data = (object?)null
            });
        }

        if (!succeeded)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Phiên đăng nhập không còn hợp lệ.",
                data = (object?)null
            });
        }

        return Ok(new
        {
            success = true,
            message = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.",
            data = (object?)null
        });
    }
}