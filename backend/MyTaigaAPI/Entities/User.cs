using System;
using System.Collections.Generic;

namespace MyTaigaAPI.Entities;

public partial class User
{
    public long UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string? Avatar { get; set; }

    public string? PhoneNumber { get; set; }

    public byte Status { get; set; }

    public int FailedLoginCount { get; set; }
    public int TokenVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();

    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public virtual ICollection<Bug> BugAssignees { get; set; } = new List<Bug>();

    public virtual ICollection<Bug> BugReporters { get; set; } = new List<Bug>();

    public virtual ICollection<ChatParticipant> ChatParticipants { get; set; } = new List<ChatParticipant>();

    public virtual ICollection<ChatRoom> ChatRoomCreatedByNavigations { get; set; } = new List<ChatRoom>();

    public virtual ICollection<ChatRoom> ChatRoomPrivateUserHighs { get; set; } = new List<ChatRoom>();

    public virtual ICollection<ChatRoom> ChatRoomPrivateUserLows { get; set; } = new List<ChatRoom>();

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual ICollection<Epic> Epics { get; set; } = new List<Epic>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual ICollection<Task> TaskAssignees { get; set; } = new List<Task>();

    public virtual ICollection<Task> TaskReporters { get; set; } = new List<Task>();

    public virtual UserRole? UserRole { get; set; }
}
