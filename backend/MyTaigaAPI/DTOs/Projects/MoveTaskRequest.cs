using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class MoveTaskRequest
{
    [Required,StringLength(30)] public string ToStatus { get; set; } = string.Empty;
    [Range(0,int.MaxValue)] public int NewPosition { get; set; }
}
