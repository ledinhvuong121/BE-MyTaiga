using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Role
{
    public long RoleId { get; set; }

    public string RoleName { get; set; } = null!;

    public string Scope { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public virtual ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
