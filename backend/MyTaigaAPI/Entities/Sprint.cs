using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Sprint
{
    public long SprintId { get; set; }

    public long ProjectId { get; set; }

    public string SprintName { get; set; } = null!;

    public string? SprintGoal { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = null!;

    public virtual ICollection<FlowMetric> FlowMetrics { get; set; } = new List<FlowMetric>();

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<SprintBacklogItem> SprintBacklogItems { get; set; } = new List<SprintBacklogItem>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
