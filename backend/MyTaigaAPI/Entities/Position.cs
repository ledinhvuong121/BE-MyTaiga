using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Position
{
    public long PositionId { get; set; }

    public long ProjectId { get; set; }

    public string PositionName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ProjectMemberPosition> ProjectMemberPositions { get; set; } = new List<ProjectMemberPosition>();
}
