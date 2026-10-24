using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Permission
{
    public long PermissionId { get; set; }

    public string PermissionCode { get; set; } = null!;

    public string? ModuleName { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
