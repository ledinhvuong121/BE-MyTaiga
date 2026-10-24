using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class VwProjectMemberDetail
{
    public long ProjectId { get; set; }

    public long UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string ProjectRole { get; set; } = null!;

    public string? DepartmentName { get; set; }

    public DateTime JoinedAt { get; set; }

    public string? Positions { get; set; }
}
