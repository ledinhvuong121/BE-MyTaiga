using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Auth;

public class UpdateProfileRequest
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên.")]
    [StringLength(100, ErrorMessage = "Họ tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(255, ErrorMessage = "Email tối đa 255 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(
        20,
        ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
    [RegularExpression(
        @"^\+?[0-9]{8,15}$",
        ErrorMessage =
            "Số điện thoại gồm 8–15 chữ số, có thể bắt đầu bằng dấu +.")]
    public string? PhoneNumber { get; set; }
}