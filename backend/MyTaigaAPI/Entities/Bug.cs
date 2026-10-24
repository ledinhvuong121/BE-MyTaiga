using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Bug
{
    public long BugId { get; set; }

    public long TaskId { get; set; }

    public long ReporterId { get; set; }

    public long? AssigneeId { get; set; }

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? Environment { get; set; }

    public string Severity { get; set; } = null!;

    public string Priority { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public virtual User? Assignee { get; set; }

    public virtual User Reporter { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
