using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Auth;

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Thiếu mã đặt lại mật khẩu.")]
    [StringLength(128)]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [MinLength(8, ErrorMessage = "Mật khẩu ít nhất 8 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
    [Compare(
        nameof(NewPassword),
        ErrorMessage = "Hai mật khẩu không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
