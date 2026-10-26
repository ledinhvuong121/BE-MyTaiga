USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER TRIGGER dbo.trg_Task_AfterInsertUpdate
ON dbo.Task AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    -- Preserve historical reporters after they leave. New/changed reporters must be members.
    IF EXISTS (
        SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.TaskId=i.TaskId
        WHERE ((d.TaskId IS NULL OR i.ReporterId<>d.ReporterId) AND NOT EXISTS
            (SELECT 1 FROM dbo.ProjectMember m WHERE m.ProjectId=i.ProjectId AND m.UserId=i.ReporterId))
        OR (i.AssigneeId IS NOT NULL AND (i.Status<>'DONE' OR d.TaskId IS NULL OR
            ISNULL(i.AssigneeId,0)<>ISNULL(d.AssigneeId,0)) AND NOT EXISTS
            (SELECT 1 FROM dbo.ProjectMember m WHERE m.ProjectId=i.ProjectId AND m.UserId=i.AssigneeId))
    )
        THROW 50050,N'Người tạo/người thực hiện mới phải là thành viên dự án.',1;

    -- Keep first start time across rework; completion is cleared when reopened.
    UPDATE t SET
        StartedAt=CASE WHEN i.Status<>'TODO' AND t.StartedAt IS NULL THEN SYSUTCDATETIME() ELSE t.StartedAt END,
        CompletedAt=CASE WHEN i.Status='DONE' THEN COALESCE(t.CompletedAt,SYSUTCDATETIME()) ELSE NULL END,
        ProgressPercent=CASE WHEN i.Status='DONE' THEN 100 WHEN d.Status='DONE' AND i.ProgressPercent=100 THEN 0 ELSE i.ProgressPercent END,
        UpdatedAt=SYSUTCDATETIME()
    FROM dbo.Task t JOIN inserted i ON i.TaskId=t.TaskId LEFT JOIN deleted d ON d.TaskId=i.TaskId
    WHERE d.TaskId IS NULL OR d.Status<>i.Status;
END;
GO
