using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class Project
{
    public long ProjectId { get; set; }

    public string ProjectName { get; set; } = null!;

    public string? Description { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string Status { get; set; } = null!;

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();

    public virtual ICollection<BoardColumn> BoardColumns { get; set; } = new List<BoardColumn>();

    public virtual ICollection<ChatRoom> ChatRooms { get; set; } = new List<ChatRoom>();

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<Department> Departments { get; set; } = new List<Department>();

    public virtual ICollection<Epic> Epics { get; set; } = new List<Epic>();

    public virtual ICollection<Position> Positions { get; set; } = new List<Position>();

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();

    public virtual Sprint? Sprint { get; set; }

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
