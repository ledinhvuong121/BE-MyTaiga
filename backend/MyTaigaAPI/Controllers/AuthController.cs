using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Services.Auth;

namespace MyTaigaAPI.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            request, cancellationToken);

        return StatusCode(result.StatusCode, new
        {
            success = result.Success,
            message = result.Message,
            data = result.Data
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(
                User.FindFirst("sub")?.Value, out var userId)
            || !int.TryParse(
                User.FindFirst("token_version")?.Value,
                out var tokenVersion))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Token không hợp lệ."
            });
        }

        var success = await _authService.LogoutAllAsync(
            userId, tokenVersion, cancellationToken);

        if (!success)
        {
            return Unauthorized(new
            {
                success = false,
                message = "Phiên đăng nhập không còn hợp lệ."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Đã đăng xuất tất cả phiên.",
            data = (object?)null
        });
    }
}