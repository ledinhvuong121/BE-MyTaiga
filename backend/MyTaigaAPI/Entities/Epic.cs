using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Epic
{
    public long EpicId { get; set; }

    public long ProjectId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public int Priority { get; set; }

    public string Status { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<ProductBacklogItem> ProductBacklogItems { get; set; } = new List<ProductBacklogItem>();

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
