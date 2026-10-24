namespace MyTaigaAPI.DTOs.Auth;

public class LoginResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public LoginResponse? Data { get; init; }
}