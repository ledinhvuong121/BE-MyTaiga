using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Services.Auth;

namespace MyTaigaAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public class PasswordResetController : ControllerBase
{
    private readonly PasswordResetService _passwordResetService;

    public PasswordResetController(
        PasswordResetService passwordResetService)
    {
        _passwordResetService = passwordResetService;
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _passwordResetService.RequestAsync(
            request.Email,
            cancellationToken);

        // Trả cùng thông báo để không tiết lộ email có tồn tại.
        return Ok(new
        {
            success = true,
            message = "Nếu email thuộc tài khoản đang hoạt động, "
                + "hướng dẫn đặt lại mật khẩu sẽ được gửi.",
            data = (object?)null
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        bool succeeded;

        try
        {
            succeeded = await _passwordResetService.ResetAsync(
                request.Token,
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
            return BadRequest(new
            {
                success = false,
                message = "Mã đặt lại mật khẩu không hợp lệ "
                    + "hoặc đã hết hiệu lực.",
                data = (object?)null
            });
        }

        return Ok(new
        {
            success = true,
            message = "Đặt lại mật khẩu thành công. "
                + "Vui lòng đăng nhập lại.",
            data = (object?)null
        });
    }
}