using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwOverdueTask
{
    public long TaskId { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public DateTime? Deadline { get; set; }

    public long? AssigneeId { get; set; }

    public int? OverdueHours { get; set; }
}
