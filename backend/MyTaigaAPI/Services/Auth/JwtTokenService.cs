using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MyTaigaAPI.DTOs.Auth;
using MyTaigaAPI.Entities;

namespace MyTaigaAPI.Services.Auth;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public LoginResponse CreateToken(
        User user,
        string systemRole,
        int expireMinutes)
    {
        if (systemRole is not ("ADMIN" or "USER"))
        {
            throw new ArgumentException("Vai trò hệ thống không hợp lệ.");
        }

        if (expireMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expireMinutes));
        }

        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Thiếu Jwt:Key.");

        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Thiếu Jwt:Issuer.");

        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Thiếu Jwt:Audience.");

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(expireMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString()),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new Claim("role", systemRole),
new Claim(
    "token_version",
    user.TokenVersion.ToString(CultureInfo.InvariantCulture))
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        return new LoginResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expiresAt,
            User = new LoginUserResponse
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                SystemRole = systemRole
            }
        };
    }
}