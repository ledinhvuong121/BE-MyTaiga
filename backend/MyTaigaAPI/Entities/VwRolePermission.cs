using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwRolePermission
{
    public string RoleName { get; set; } = null!;

    public string Scope { get; set; } = null!;

    public string PermissionCode { get; set; } = null!;

    public string? ModuleName { get; set; }

    public string? Description { get; set; }
}
