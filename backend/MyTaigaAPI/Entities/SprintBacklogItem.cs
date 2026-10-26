using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class SprintBacklogItem
{
    public long SprintId { get; set; }

    public long Pbiid { get; set; }

    public DateTime AddedAt { get; set; }

    public int? StoryPointAtClose { get; set; }

    public string? StatusAtClose { get; set; }

    public virtual ProductBacklogItem Pbi { get; set; } = null!;

    public virtual Sprint Sprint { get; set; } = null!;
}
