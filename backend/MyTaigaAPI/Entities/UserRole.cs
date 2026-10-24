using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class UserRole
{
    public long UserId { get; set; }

    public long RoleId { get; set; }

    public string RoleScope { get; set; } = null!;

    public virtual Role Role { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
