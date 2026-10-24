using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using MyTaigaAPI.Entities;
using Task = MyTaigaAPI.Entities.Task;

namespace MyTaigaAPI.Data;

public partial class ScrumbanDbContext : DbContext
{
    public ScrumbanDbContext(DbContextOptions<ScrumbanDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActivityLog> ActivityLogs { get; set; }

    public virtual DbSet<Attachment> Attachments { get; set; }

    public virtual DbSet<BoardColumn> BoardColumns { get; set; }

    public virtual DbSet<Bug> Bugs { get; set; }

    public virtual DbSet<ChatParticipant> ChatParticipants { get; set; }

    public virtual DbSet<ChatRoom> ChatRooms { get; set; }

    public virtual DbSet<Comment> Comments { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Epic> Epics { get; set; }

    public virtual DbSet<FlowMetric> FlowMetrics { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageAttachment> MessageAttachments { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<Position> Positions { get; set; }

    public virtual DbSet<ProductBacklogItem> ProductBacklogItems { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectMember> ProjectMembers { get; set; }

    public virtual DbSet<ProjectMemberPosition> ProjectMemberPositions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Sprint> Sprints { get; set; }

    public virtual DbSet<SprintBacklogItem> SprintBacklogItems { get; set; }

    public virtual DbSet<SystemConfig> SystemConfigs { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<VwBoardColumnWip> VwBoardColumnWips { get; set; }

    public virtual DbSet<VwOverdueTask> VwOverdueTasks { get; set; }

    public virtual DbSet<VwProjectFlowSummary> VwProjectFlowSummaries { get; set; }

    public virtual DbSet<VwProjectMemberDetail> VwProjectMemberDetails { get; set; }

    public virtual DbSet<VwRolePermission> VwRolePermissions { get; set; }

    public virtual DbSet<VwSprintThroughput> VwSprintThroughputs { get; set; }

    public virtual DbSet<VwTaskFlow> VwTaskFlows { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.ToTable("ActivityLog");

            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "IX_Log_Entity");

            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }, "IX_Log_Project").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_Log_User").IsDescending(false, true);

            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.EntityType)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.Project).WithMany(p => p.ActivityLogs)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_Log_Project");

            entity.HasOne(d => d.User).WithMany(p => p.ActivityLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_Log_User");
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.ToTable("Attachment");

            entity.HasIndex(e => e.TaskId, "IX_Attachment_Task");

            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FilePath)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.UploadedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Task).WithMany(p => p.Attachments)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("FK_Att_Task");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.Attachments)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Att_Uploader");
        });

        modelBuilder.Entity<BoardColumn>(entity =>
        {
            entity.ToTable("BoardColumn");

            entity.HasIndex(e => new { e.ProjectId, e.ColumnName }, "UQ_BoardColumn_Name").IsUnique();

            entity.Property(e => e.ColumnName)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.Project).WithMany(p => p.BoardColumns)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_BoardColumn_Project");
        });

        modelBuilder.Entity<Bug>(entity =>
        {
            entity.ToTable("Bug", tb => tb.HasTrigger("trg_Bug_AfterUpdate"));

            entity.HasIndex(e => e.AssigneeId, "IX_Bug_Assignee").HasFilter("([AssigneeId] IS NOT NULL)");

            entity.HasIndex(e => new { e.Status, e.Severity }, "IX_Bug_Filter");

            entity.HasIndex(e => e.TaskId, "IX_Bug_Task");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Environment).HasMaxLength(500);
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("MEDIUM");
            entity.Property(e => e.ResolvedAt).HasPrecision(0);
            entity.Property(e => e.Severity)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("OPEN");
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Assignee).WithMany(p => p.BugAssignees)
                .HasForeignKey(d => d.AssigneeId)
                .HasConstraintName("FK_Bug_Assignee");

            entity.HasOne(d => d.Reporter).WithMany(p => p.BugReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Bug_Reporter");

            entity.HasOne(d => d.Task).WithMany(p => p.Bugs)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("FK_Bug_Task");
        });

        modelBuilder.Entity<ChatParticipant>(entity =>
        {
            entity.HasKey(e => new { e.ChatRoomId, e.UserId });

            entity.ToTable("ChatParticipant");

            entity.HasIndex(e => e.UserId, "IX_CP_User");

            entity.Property(e => e.JoinedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.LastReadAt).HasPrecision(0);

            entity.HasOne(d => d.ChatRoom).WithMany(p => p.ChatParticipants)
                .HasForeignKey(d => d.ChatRoomId)
                .HasConstraintName("FK_CP_Room");

            entity.HasOne(d => d.User).WithMany(p => p.ChatParticipants)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CP_User");
        });

        modelBuilder.Entity<ChatRoom>(entity =>
        {
            entity.ToTable("ChatRoom");

            entity.HasIndex(e => e.ProjectId, "IX_Room_Project").HasFilter("([ProjectId] IS NOT NULL)");

            entity.HasIndex(e => new { e.PrivateUserLowId, e.PrivateUserHighId }, "UX_Room_PrivatePair")
                .IsUnique()
                .HasFilter("([RoomType]='PRIVATE')");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.RoomName).HasMaxLength(100);
            entity.Property(e => e.RoomType)
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.ChatRoomCreatedByNavigations)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("FK_Room_Creator");

            entity.HasOne(d => d.PrivateUserHigh).WithMany(p => p.ChatRoomPrivateUserHighs)
                .HasForeignKey(d => d.PrivateUserHighId)
                .HasConstraintName("FK_Room_High");

            entity.HasOne(d => d.PrivateUserLow).WithMany(p => p.ChatRoomPrivateUserLows)
                .HasForeignKey(d => d.PrivateUserLowId)
                .HasConstraintName("FK_Room_Low");

            entity.HasOne(d => d.Project).WithMany(p => p.ChatRooms)
                .HasForeignKey(d => d.ProjectId)
                .HasConstraintName("FK_Room_Project");
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.ToTable("Comment");

            entity.HasIndex(e => new { e.TaskId, e.CreatedAt }, "IX_Comment_Task");

            entity.HasIndex(e => e.UserId, "IX_Comment_User");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Task).WithMany(p => p.Comments)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("FK_Comment_Task");

            entity.HasOne(d => d.User).WithMany(p => p.Comments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Comment_User");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Department");

            entity.HasIndex(e => new { e.DepartmentId, e.ProjectId }, "UQ_Department_Id_Proj").IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.DepartmentName }, "UQ_Department_Name").IsUnique();

            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasOne(d => d.Project).WithMany(p => p.Departments)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Department_Project");
        });

        modelBuilder.Entity<Epic>(entity =>
        {
            entity.ToTable("Epic");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.Priority }, "IX_Epic_Project");

            entity.HasIndex(e => new { e.EpicId, e.ProjectId }, "UQ_Epic_Id_Proj").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Priority).HasDefaultValue(3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("OPEN");
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Epics)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Epic_Creator");

            entity.HasOne(d => d.Project).WithMany(p => p.Epics)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Epic_Project");
        });

        modelBuilder.Entity<FlowMetric>(entity =>
        {
            entity.ToTable("FlowMetric");

            entity.HasIndex(e => new { e.SprintId, e.CapturedAt }, "IX_FM_Sprint").IsDescending(false, true);

            entity.Property(e => e.AvgWorkItemAge).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CapturedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.CycleTime).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Wip).HasColumnName("WIP");

            entity.HasOne(d => d.Sprint).WithMany(p => p.FlowMetrics)
                .HasForeignKey(d => d.SprintId)
                .HasConstraintName("FK_FM_Sprint");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("Message");

            entity.HasIndex(e => new { e.ChatRoomId, e.SentAt }, "IX_Msg_Room").IsDescending(false, true);

            entity.Property(e => e.SentAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.ChatRoom).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ChatRoomId)
                .HasConstraintName("FK_Msg_Room");

            entity.HasOne(d => d.ChatParticipant).WithMany(p => p.Messages)
                .HasForeignKey(d => new { d.ChatRoomId, d.SenderId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Msg_Sender");
        });

        modelBuilder.Entity<MessageAttachment>(entity =>
        {
            entity.ToTable("MessageAttachment");

            entity.HasIndex(e => e.MessageId, "IX_MA_Message");

            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.FilePath)
                .HasMaxLength(500)
                .IsUnicode(false);

            entity.HasOne(d => d.Message).WithMany(p => p.MessageAttachments)
                .HasForeignKey(d => d.MessageId)
                .HasConstraintName("FK_MA_Message");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notification");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_Noti_User").IsDescending(false, false, true);

            entity.Property(e => e.Content).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ReadAt).HasPrecision(0);
            entity.Property(e => e.RefType)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Type)
                .HasMaxLength(30)
                .IsUnicode(false);

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Noti_User");
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.TokenId);

            entity.ToTable("PasswordResetToken");

            entity.HasIndex(e => new { e.UserId, e.IsUsed, e.ExpiresAt }, "IX_PRT_User");

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ExpiresAt).HasPrecision(0);
            entity.Property(e => e.TokenHash)
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_PRT_User");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permission");

            entity.HasIndex(e => e.PermissionCode, "UQ_Permission_Code").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.ModuleName).HasMaxLength(100);
            entity.Property(e => e.PermissionCode)
                .HasMaxLength(60)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.ToTable("Position");

            entity.HasIndex(e => new { e.PositionId, e.ProjectId }, "UQ_Position_Id_Proj").IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.PositionName }, "UQ_Position_Name").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.PositionName).HasMaxLength(100);

            entity.HasOne(d => d.Project).WithMany(p => p.Positions)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Position_Project");
        });

        modelBuilder.Entity<ProductBacklogItem>(entity =>
        {
            entity.HasKey(e => e.Pbiid).HasName("PK_PBI");

            entity.ToTable("ProductBacklogItem");

            entity.HasIndex(e => new { e.EpicId, e.Priority }, "IX_PBI_Epic");

            entity.Property(e => e.Pbiid).HasColumnName("PBIId");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Priority).HasDefaultValue(3);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("NEW");
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Epic).WithMany(p => p.ProductBacklogItems)
                .HasForeignKey(d => d.EpicId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PBI_Epic");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Project", tb => tb.HasTrigger("trg_Project_AfterInsert"));

            entity.HasIndex(e => e.Status, "IX_Project_Status");

            entity.HasIndex(e => new { e.CreatedBy, e.ProjectName }, "UQ_Project_Name").IsUnique();

            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ProjectName).HasMaxLength(200);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("ACTIVE");
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.Projects)
                .HasForeignKey(d => d.CreatedBy)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Project_Creator");
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.UserId });

            entity.ToTable("ProjectMember", tb => tb.HasTrigger("trg_ProjectMember_KeepManager"));

            entity.HasIndex(e => e.UserId, "IX_PM_User");

            entity.Property(e => e.JoinedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.RoleScope)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("PROJECT");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectMembers)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PM_Project");

            entity.HasOne(d => d.User).WithMany(p => p.ProjectMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PM_User");

            entity.HasOne(d => d.Department).WithMany(p => p.ProjectMembers)
                .HasPrincipalKey(p => new { p.DepartmentId, p.ProjectId })
                .HasForeignKey(d => new { d.DepartmentId, d.ProjectId })
                .HasConstraintName("FK_PM_Department");

            entity.HasOne(d => d.Role).WithMany(p => p.ProjectMembers)
                .HasPrincipalKey(p => new { p.RoleId, p.Scope })
                .HasForeignKey(d => new { d.RoleId, d.RoleScope })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PM_Role");
        });

        modelBuilder.Entity<ProjectMemberPosition>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.UserId, e.PositionId }).HasName("PK_PMP");

            entity.ToTable("ProjectMemberPosition");

            entity.HasOne(d => d.Position).WithMany(p => p.ProjectMemberPositions)
                .HasPrincipalKey(p => new { p.PositionId, p.ProjectId })
                .HasForeignKey(d => new { d.PositionId, d.ProjectId })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PMP_Position");

            entity.HasOne(d => d.ProjectMember).WithMany(p => p.ProjectMemberPositions)
                .HasForeignKey(d => new { d.ProjectId, d.UserId })
                .HasConstraintName("FK_PMP_Member");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role");

            entity.HasIndex(e => new { e.RoleId, e.Scope }, "UQ_Role_Id_Scope").IsUnique();

            entity.HasIndex(e => e.RoleName, "UQ_Role_RoleName").IsUnique();

            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.RoleName)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Scope)
                .HasMaxLength(10)
                .IsUnicode(false);

            entity.HasMany(d => d.Permissions).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .HasConstraintName("FK_RP_Permission"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .HasConstraintName("FK_RP_Role"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionId");
                        j.ToTable("RolePermission");
                        j.HasIndex(new[] { "PermissionId" }, "IX_RP_Permission");
                    });
        });

        modelBuilder.Entity<Sprint>(entity =>
        {
            entity.ToTable("Sprint", tb => tb.HasTrigger("trg_Sprint_NoOverlap"));

            entity.HasIndex(e => new { e.SprintId, e.ProjectId }, "UQ_Sprint_Id_Proj").IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.SprintName }, "UQ_Sprint_Name").IsUnique();

            entity.HasIndex(e => e.ProjectId, "UX_Sprint_OneActive")
                .IsUnique()
                .HasFilter("([Status]='ACTIVE')");

            entity.Property(e => e.SprintGoal).HasMaxLength(500);
            entity.Property(e => e.SprintName).HasMaxLength(150);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("PLANNED");

            entity.HasOne(d => d.Project).WithOne(p => p.Sprint)
                .HasForeignKey<Sprint>(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Sprint_Project");
        });

        modelBuilder.Entity<SprintBacklogItem>(entity =>
        {
            entity.HasKey(e => new { e.SprintId, e.Pbiid }).HasName("PK_SBI");

            entity.ToTable("SprintBacklogItem");

            entity.HasIndex(e => e.Pbiid, "IX_SBI_PBI");

            entity.Property(e => e.Pbiid).HasColumnName("PBIId");
            entity.Property(e => e.AddedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Pbi).WithMany(p => p.SprintBacklogItems)
                .HasForeignKey(d => d.Pbiid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SBI_PBI");

            entity.HasOne(d => d.Sprint).WithMany(p => p.SprintBacklogItems)
                .HasForeignKey(d => d.SprintId)
                .HasConstraintName("FK_SBI_Sprint");
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(e => e.ConfigKey);

            entity.ToTable("SystemConfig");

            entity.Property(e => e.ConfigKey)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.ConfigValue).HasMaxLength(500);
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.ToTable("Task", tb => tb.HasTrigger("trg_Task_AfterInsertUpdate"));

            entity.HasIndex(e => new { e.AssigneeId, e.Status }, "IX_Task_Assignee").HasFilter("([AssigneeId] IS NOT NULL)");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.Position }, "IX_Task_Board");

            entity.HasIndex(e => e.Deadline, "IX_Task_Deadline").HasFilter("([Deadline] IS NOT NULL AND [CompletedAt] IS NULL)");

            entity.HasIndex(e => e.EpicId, "IX_Task_Epic").HasFilter("([EpicId] IS NOT NULL)");

            entity.HasIndex(e => e.ParentTaskId, "IX_Task_Parent").HasFilter("([ParentTaskId] IS NOT NULL)");

            entity.HasIndex(e => e.SprintId, "IX_Task_Sprint").HasFilter("([SprintId] IS NOT NULL)");

            entity.HasIndex(e => new { e.TaskId, e.ProjectId }, "UQ_Task_Id_Proj").IsUnique();

            entity.Property(e => e.CompletedAt).HasPrecision(0);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Deadline).HasPrecision(0);
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("MEDIUM");
            entity.Property(e => e.StartedAt).HasPrecision(0);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false)
                .HasDefaultValue("TODO");
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);

            entity.HasOne(d => d.Assignee).WithMany(p => p.TaskAssignees)
                .HasForeignKey(d => d.AssigneeId)
                .HasConstraintName("FK_Task_Assignee");

            entity.HasOne(d => d.Project).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Task_Project");

            entity.HasOne(d => d.Reporter).WithMany(p => p.TaskReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Task_Reporter");

            entity.HasOne(d => d.Epic).WithMany(p => p.Tasks)
                .HasPrincipalKey(p => new { p.EpicId, p.ProjectId })
                .HasForeignKey(d => new { d.EpicId, d.ProjectId })
                .HasConstraintName("FK_Task_Epic");

            entity.HasOne(d => d.TaskNavigation).WithMany(p => p.InverseTaskNavigation)
                .HasPrincipalKey(p => new { p.TaskId, p.ProjectId })
                .HasForeignKey(d => new { d.ParentTaskId, d.ProjectId })
                .HasConstraintName("FK_Task_Parent");

            entity.HasOne(d => d.BoardColumn).WithMany(p => p.Tasks)
                .HasPrincipalKey(p => new { p.ProjectId, p.ColumnName })
                .HasForeignKey(d => new { d.ProjectId, d.Status })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Task_Column");

            entity.HasOne(d => d.Sprint).WithMany(p => p.Tasks)
                .HasPrincipalKey(p => new { p.SprintId, p.ProjectId })
                .HasForeignKey(d => new { d.SprintId, d.ProjectId })
                .HasConstraintName("FK_Task_Sprint");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");

            entity.HasIndex(e => e.Email, "UQ_User_Email").IsUnique();

            entity.Property(e => e.Avatar)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.PhoneNumber)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status).HasDefaultValue((byte)1);
            entity.Property(e => e.UpdatedAt).HasPrecision(0);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.ToTable("UserRole", tb => tb.HasTrigger("trg_UserRole_SingleAdmin"));

            entity.HasIndex(e => e.UserId, "UQ_UserRole_User").IsUnique();

            entity.Property(e => e.RoleScope)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasDefaultValue("SYSTEM");

            entity.HasOne(d => d.User).WithOne(p => p.UserRole)
                .HasForeignKey<UserRole>(d => d.UserId)
                .HasConstraintName("FK_UserRole_User");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasPrincipalKey(p => new { p.RoleId, p.Scope })
                .HasForeignKey(d => new { d.RoleId, d.RoleScope })
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserRole_Role");
        });

        modelBuilder.Entity<VwBoardColumnWip>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_BoardColumnWip");

            entity.Property(e => e.ColumnName)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwOverdueTask>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_OverdueTasks");

            entity.Property(e => e.Deadline).HasPrecision(0);
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TaskId).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).HasMaxLength(255);
        });

        modelBuilder.Entity<VwProjectFlowSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_ProjectFlowSummary");

            entity.Property(e => e.AvgCycleTimeDays).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.AvgWorkItemAgeDays).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.ProjectName).HasMaxLength(200);
            entity.Property(e => e.Wip).HasColumnName("WIP");
        });

        modelBuilder.Entity<VwProjectMemberDetail>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_ProjectMemberDetail");

            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.JoinedAt).HasPrecision(0);
            entity.Property(e => e.Positions).HasMaxLength(4000);
            entity.Property(e => e.ProjectRole)
                .HasMaxLength(30)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwRolePermission>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_RolePermission");

            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.ModuleName).HasMaxLength(100);
            entity.Property(e => e.PermissionCode)
                .HasMaxLength(60)
                .IsUnicode(false);
            entity.Property(e => e.RoleName)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.Scope)
                .HasMaxLength(10)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwSprintThroughput>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_SprintThroughput");

            entity.Property(e => e.AvgCycleTimeDays).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.SprintName).HasMaxLength(150);
            entity.Property(e => e.SprintStatus)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwTaskFlow>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TaskFlow");

            entity.Property(e => e.CompletedAt).HasPrecision(0);
            entity.Property(e => e.CycleTimeDays).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.StartedAt).HasPrecision(0);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsUnicode(false);
            entity.Property(e => e.TaskId).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).HasMaxLength(255);
            entity.Property(e => e.WorkItemAgeDays).HasColumnType("decimal(10, 2)");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
