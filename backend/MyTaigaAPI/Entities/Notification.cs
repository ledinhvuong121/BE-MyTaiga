using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Notification
{
    public long NotificationId { get; set; }

    public long UserId { get; set; }

    public string Type { get; set; } = null!;

    public string? Title { get; set; }

    public string? Content { get; set; }

    public string? RefType { get; set; }

    public long? RefId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime? PushedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
