using System.ComponentModel.DataAnnotations;

namespace MyTaigaAPI.DTOs.Projects;

public class TaskUpdateRequest
{
    public string[]? Tags { get; set; }
    private int? storyPoints;
    [System.Text.Json.Serialization.JsonIgnore] public bool StoryPointsSpecified { get; private set; }
    [Range(0,10000)] public int? StoryPoints { get=>storyPoints; set{storyPoints=value;StoryPointsSpecified=true;} }
    [Required, StringLength(255)] public string Title { get; set; } = string.Empty;
    [StringLength(10000)] public string? Description { get; set; }
}
public class TaskCreateRequest : TaskUpdateRequest
{
    [Range(1, long.MaxValue)] public long? SprintId { get; set; }
    [Range(1, long.MaxValue)] public long? EpicId { get; set; }
    [Range(1, long.MaxValue)] public long? ParentTaskId { get; set; }
    [Range(1, long.MaxValue)] public long? AssigneeId { get; set; }
    [Required, RegularExpression("^(LOW|MEDIUM|HIGH|CRITICAL)$")] public string Priority { get; set; } = "MEDIUM";
    public DateTimeOffset? Deadline { get; set; }
}
public class AssignTaskRequest
{
    [Range(1, long.MaxValue)] public long? AssigneeId { get; set; }
}
public class TaskPriorityRequest
{
    [Required, RegularExpression("^(LOW|MEDIUM|HIGH|CRITICAL)$")] public string Priority { get; set; } = string.Empty;
}
public class TaskDeadlineRequest { public DateTimeOffset? Deadline { get; set; } }
public class TaskProgressRequest
{
    [Range(0,99)] public int ProgressPercent { get; set; }
}
