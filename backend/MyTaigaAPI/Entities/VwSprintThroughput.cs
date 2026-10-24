using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwSprintThroughput
{
    public long ProjectId { get; set; }

    public long SprintId { get; set; }

    public string SprintName { get; set; } = null!;

    public string SprintStatus { get; set; } = null!;

    public int? TotalTasks { get; set; }

    public int? DoneTasks { get; set; }

    public decimal? AvgCycleTimeDays { get; set; }
}
