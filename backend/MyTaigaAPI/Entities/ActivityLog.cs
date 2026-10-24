using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ActivityLog
{
    public long ActivityLogId { get; set; }

    public long? ProjectId { get; set; }

    public long? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string? EntityType { get; set; }

    public long? EntityId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Project? Project { get; set; }

    public virtual User? User { get; set; }
}
