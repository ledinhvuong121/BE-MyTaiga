using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Comment
{
    public long CommentId { get; set; }

    public long TaskId { get; set; }

    public long UserId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Task Task { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
