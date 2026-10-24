using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwTaskFlow
{
    public long TaskId { get; set; }

    public long ProjectId { get; set; }

    public long? SprintId { get; set; }

    public string Title { get; set; } = null!;

    public string Status { get; set; } = null!;

    public long? AssigneeId { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public decimal? CycleTimeDays { get; set; }

    public decimal? WorkItemAgeDays { get; set; }
}
