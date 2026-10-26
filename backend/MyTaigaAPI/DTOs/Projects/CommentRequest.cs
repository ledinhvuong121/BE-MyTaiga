using System.ComponentModel.DataAnnotations;
namespace MyTaigaAPI.DTOs.Projects;
public class CommentRequest
{
    [Required,StringLength(5000)] public string Content { get; set; } = string.Empty;
}
