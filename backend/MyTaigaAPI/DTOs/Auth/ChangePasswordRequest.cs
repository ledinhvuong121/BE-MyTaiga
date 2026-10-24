using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Auth;

public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [MinLength(8, ErrorMessage = "Mật khẩu mới cần ít nhất 8 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới.")]
    [Compare(
        nameof(NewPassword),
        ErrorMessage = "Hai mật khẩu mới không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
