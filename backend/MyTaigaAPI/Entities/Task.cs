using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Task
{
    public long TaskId { get; set; }

    public long ProjectId { get; set; }

    public long? SprintId { get; set; }

    public long? EpicId { get; set; }

    public long? ParentTaskId { get; set; }

    public long? AssigneeId { get; set; }

    public long ReporterId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Priority { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? Deadline { get; set; }

    public int ProgressPercent { get; set; }

    public int Position { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User? Assignee { get; set; }

    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public virtual BoardColumn BoardColumn { get; set; } = null!;

    public virtual ICollection<Bug> Bugs { get; set; } = new List<Bug>();

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual Epic? Epic { get; set; }

    public virtual ICollection<Task> InverseTaskNavigation { get; set; } = new List<Task>();

    public virtual Project Project { get; set; } = null!;

    public virtual User Reporter { get; set; } = null!;

    public virtual Sprint? Sprint { get; set; }

    public virtual Task? TaskNavigation { get; set; }
}
