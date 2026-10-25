using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class DepartmentRequest
{
    [Required, StringLength(100)]
    public string DepartmentName { get; set; } = string.Empty;
    [StringLength(500)]
    public string? Description { get; set; }
}

public class AssignDepartmentRequest
{
    // null removes the assignment; any non-null id must belong to this project.
    [Range(1, long.MaxValue)]
    public long? DepartmentId { get; set; }
}
