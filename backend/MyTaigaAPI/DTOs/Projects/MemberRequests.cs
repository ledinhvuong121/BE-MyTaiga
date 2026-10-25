using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class AddProjectMemberRequest
{
    [Range(1, long.MaxValue)]
    public long UserId { get; set; }
}

public class PromoteProjectMemberRequest
{
    [Required, RegularExpression("^MANAGER$", ErrorMessage = "Chỉ hỗ trợ nâng vai trò thành MANAGER.")]
    public string Role { get; set; } = string.Empty;
}
