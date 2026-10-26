using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class BugUpdateRequest
{
    [Required,StringLength(255)] public string Title { get; set; } = string.Empty;
    [Required,StringLength(10000)] public string Description { get; set; } = string.Empty;
    [StringLength(500)] public string? Environment { get; set; }
    [Required,RegularExpression("^(TRIVIAL|MINOR|MAJOR|BLOCKER)$")] public string Severity { get; set; } = string.Empty;
    [Required,RegularExpression("^(LOW|MEDIUM|HIGH|CRITICAL)$")] public string Priority { get; set; } = "MEDIUM";
}
public class BugCreateRequest : BugUpdateRequest
{
    [Range(1,long.MaxValue)] public long TaskId { get; set; }
}
public class BugAssigneeRequest
{
    [Range(1,long.MaxValue)] public long? AssigneeId { get; set; }
}
public class BugStatusRequest
{
    [Required,RegularExpression("^(OPEN|IN_PROGRESS|RESOLVED|CLOSED|REOPENED)$")]
    public string Status { get; set; } = string.Empty;
}
public class BugPriorityRequest
{
    [Required,RegularExpression("^(LOW|MEDIUM|HIGH|CRITICAL)$")]
    public string Priority { get; set; } = string.Empty;
}
