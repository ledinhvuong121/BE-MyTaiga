USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Notification','PushedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Notification ADD PushedAt DATETIME2(7) NULL;
    -- Keep historical notifications in the inbox, but do not replay them all as live events on upgrade.
    EXEC sys.sp_executesql N'UPDATE dbo.Notification SET PushedAt=SYSUTCDATETIME();';
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Notification') AND name='IX_Notification_Unpushed')
    CREATE INDEX IX_Notification_Unpushed ON dbo.Notification(PushedAt,NotificationId) WHERE PushedAt IS NULL;
GO
CREATE OR ALTER TRIGGER dbo.trg_Project_AfterInsert
ON dbo.Project AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.UserRole ur ON ur.UserId=i.CreatedBy
        JOIN dbo.Role r ON r.RoleId=ur.RoleId WHERE r.RoleName='ADMIN')
        THROW 50010,N'Admin chỉ quản trị hệ thống, không được tạo dự án.',1;
    INSERT dbo.BoardColumn(ProjectId,ColumnName,Position,WipLimit)
    SELECT i.ProjectId,c.ColumnName,c.Position,NULL FROM inserted i
    CROSS JOIN(VALUES('TODO',1),('IN_PROGRESS',2),('IN_REVIEW',3),('DONE',4)) c(ColumnName,Position);
    INSERT dbo.ProjectMember(ProjectId,UserId,RoleId,JoinedAt)
    SELECT i.ProjectId,i.CreatedBy,r.RoleId,SYSUTCDATETIME() FROM inserted i
    JOIN dbo.Role r ON r.RoleName='MANAGER' AND r.Scope='PROJECT';
    INSERT dbo.ActivityLog(ProjectId,UserId,Action,EntityType,EntityId,Description,CreatedAt)
    SELECT i.ProjectId,i.CreatedBy,'PROJECT_CREATED','PROJECT',i.ProjectId,N'Tạo dự án '+i.ProjectName,SYSUTCDATETIME() FROM inserted i;
END;
GO
COMMIT TRANSACTION;
GO
