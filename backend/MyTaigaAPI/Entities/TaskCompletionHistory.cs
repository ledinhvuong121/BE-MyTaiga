namespace MyTaigaAPI.Entities;

// Immutable first completion, intentionally retained if the Task is later deleted.
public class TaskCompletionHistory
{
    public long TaskId { get; set; }
    public long ProjectId { get; set; }
    public long? SprintId { get; set; }
    public DateTime CompletedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public bool IsBaseline { get; set; }
}
