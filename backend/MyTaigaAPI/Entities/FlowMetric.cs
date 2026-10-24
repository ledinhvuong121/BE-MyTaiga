using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class FlowMetric
{
    public long FlowMetricId { get; set; }

    public long SprintId { get; set; }

    public int Wip { get; set; }

    public int Throughput { get; set; }

    public decimal? CycleTime { get; set; }

    public decimal? AvgWorkItemAge { get; set; }

    public DateTime CapturedAt { get; set; }

    public virtual Sprint Sprint { get; set; } = null!;
}
