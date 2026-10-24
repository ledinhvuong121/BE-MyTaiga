namespace MyTaigaAPI.DTOs.Auth;

public class LoginUserResponse
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SystemRole { get; set; } = string.Empty;
}