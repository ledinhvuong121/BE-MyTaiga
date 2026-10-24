using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class ProjectMember
{
    public long ProjectId { get; set; }

    public long UserId { get; set; }

    public long RoleId { get; set; }

    public string RoleScope { get; set; } = null!;

    public long? DepartmentId { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual Department? Department { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ProjectMemberPosition> ProjectMemberPositions { get; set; } = new List<ProjectMemberPosition>();

    public virtual Role Role { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
