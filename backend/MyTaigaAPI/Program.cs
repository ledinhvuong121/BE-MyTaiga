using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyTaigaAPI.Data;
using MyTaigaAPI.Services;
using MyTaigaAPI.Services.Auth;
using MyTaigaAPI.Services.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ScrumbanDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ScrumbanDb")
        ?? throw new InvalidOperationException(
            "Chưa cấu hình ConnectionStrings:ScrumbanDb.")));

var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(jwtKey)
    || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key chưa được cấu hình hoặc ngắn hơn 32 byte.");
}

if (string.IsNullOrWhiteSpace(jwtIssuer)
    || string.IsNullOrWhiteSpace(jwtAudience))
{
    throw new InvalidOperationException(
        "Chưa cấu hình Jwt:Issuer hoặc Jwt:Audience.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?
                    .FindFirst("sub")?.Value;

                var versionClaim = context.Principal?
                    .FindFirst("token_version")?.Value;

                if (!long.TryParse(userIdClaim, out var userId)
                    || !int.TryParse(versionClaim, out var tokenVersion)
                    || tokenVersion < 0)
                {
                    context.Fail("Token không hợp lệ.");
                    return;
                }

                var db = context.HttpContext.RequestServices
                    .GetRequiredService<ScrumbanDbContext>();

                var isValid = await db.Users
                    .AsNoTracking()
                    .AnyAsync(
                        user => user.UserId == userId
                            && user.Status == 1
                            && user.TokenVersion == tokenVersion,
                        context.HttpContext.RequestAborted);

                if (!isValid)
                {
                    context.Fail(
                        "Tài khoản bị khóa hoặc token đã bị thu hồi.");
                }
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<SystemConfigService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AvatarImageService>();
builder.Services.AddScoped<TaskFileStore>();
var emailProvider = builder.Configuration["Email:Provider"]
    ?? "Development";

if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else if (
    emailProvider.Equals("Development", StringComparison.OrdinalIgnoreCase)
    && builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IEmailSender, DevelopmentEmailSender>();
}
else
{
    throw new InvalidOperationException(
        "Cấu hình Email:Provider không hợp lệ. "
        + "Môi trường ngoài Development phải cấu hình Smtp.");
}

builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<ChangePasswordService>();
builder.Services.AddScoped<RegisterService>();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
