using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwProjectFlowSummary
{
    public long ProjectId { get; set; }

    public string ProjectName { get; set; } = null!;

    public int? Wip { get; set; }

    public int? Throughput7Days { get; set; }

    public decimal? AvgCycleTimeDays { get; set; }

    public decimal? AvgWorkItemAgeDays { get; set; }
}
