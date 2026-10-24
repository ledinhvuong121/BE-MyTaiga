using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ProjectMemberPosition
{
    public long ProjectId { get; set; }

    public long UserId { get; set; }

    public long PositionId { get; set; }

    public virtual Position Position { get; set; } = null!;

    public virtual ProjectMember ProjectMember { get; set; } = null!;
}
