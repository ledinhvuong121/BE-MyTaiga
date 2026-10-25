using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Services.Auth;

namespace MyTaigaAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public class RegisterController : ControllerBase
{
    private readonly RegisterService _service;

    public RegisterController(RegisterService service)
    {
        _service = service;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RegisterAsync(
            request,
            cancellationToken);

        return StatusCode(result.StatusCode, new
        {
            success = result.StatusCode == 201,
            message = result.Message,
            data = result.UserId.HasValue
                ? new
                {
                    userId = result.UserId.Value,
                    systemRole = "USER"
                }
                : null
        });
    }
}