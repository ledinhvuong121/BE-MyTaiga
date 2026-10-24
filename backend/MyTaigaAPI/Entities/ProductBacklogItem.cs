using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ProductBacklogItem
{
    public long Pbiid { get; set; }

    public long EpicId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int StoryPoint { get; set; }

    public int Priority { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Epic Epic { get; set; } = null!;

    public virtual ICollection<SprintBacklogItem> SprintBacklogItems { get; set; } = new List<SprintBacklogItem>();
}
