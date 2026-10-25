using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class BacklogRequest
{
    [Range(1, long.MaxValue)] public long EpicId { get; set; }
    [Required, StringLength(255)] public string Title { get; set; } = string.Empty;
    [StringLength(10000)] public string? Description { get; set; }
    [Range(0, int.MaxValue)] public int StoryPoint { get; set; }
    [Range(1, int.MaxValue)] public int Priority { get; set; } = 3;
    [Required, RegularExpression("^(NEW|READY|IN_SPRINT|DONE)$")]
    public string Status { get; set; } = "NEW";
}

public class BacklogPriorityRequest
{
    [Range(1, int.MaxValue)] public int Priority { get; set; }
}

public class BacklogOrderRequest
{
    [Range(1, long.MaxValue)] public long EpicId { get; set; }
    [Required, MinLength(1), MaxLength(1000)] public List<long> PbiIds { get; set; } = [];
}
