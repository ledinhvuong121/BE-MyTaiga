using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MyTaigaAPI.DTOs.Auth;

public class UpdateAvatarRequest
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh đại diện.")]
    public IFormFile File { get; set; } = null!;
}