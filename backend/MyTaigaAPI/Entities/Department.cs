using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Department
{
    public long DepartmentId { get; set; }

    public long ProjectId { get; set; }

    public string DepartmentName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual Project Project { get; set; } = null!;

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();
}
