using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class EpicRequest
{
    [Required, StringLength(255)]
    public string Title { get; set; } = string.Empty;
    [StringLength(10000)]
    public string? Description { get; set; }
    [Range(1, int.MaxValue)]
    public int Priority { get; set; } = 3;
    [Required, RegularExpression("^(OPEN|IN_PROGRESS|DONE|CLOSED)$")]
    public string Status { get; set; } = "OPEN";
}

public class LinkEpicRequest
{
    [Range(1, long.MaxValue)]
    public long EpicId { get; set; }
}
