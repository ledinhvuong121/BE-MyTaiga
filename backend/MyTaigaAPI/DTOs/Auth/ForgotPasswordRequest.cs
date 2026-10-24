using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Auth;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;
}
