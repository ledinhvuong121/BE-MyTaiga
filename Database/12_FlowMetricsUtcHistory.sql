USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
IF OBJECT_ID('dbo.TaskCompletionHistory','U') IS NULL
BEGIN
    CREATE TABLE dbo.TaskCompletionHistory (
        TaskId BIGINT NOT NULL PRIMARY KEY,
        ProjectId BIGINT NOT NULL,
        SprintId BIGINT NULL,
        CompletedAt DATETIME2(7) NOT NULL,
        StartedAt DATETIME2(7) NULL,
        IsBaseline BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_Completion_Project FOREIGN KEY(ProjectId) REFERENCES dbo.Project(ProjectId)
    );
    CREATE INDEX IX_Completion_ProjectDate ON dbo.TaskCompletionHistory(ProjectId,CompletedAt) INCLUDE(SprintId,IsBaseline);
    INSERT dbo.TaskCompletionHistory(TaskId,ProjectId,SprintId,CompletedAt,StartedAt,IsBaseline)
    SELECT TaskId,ProjectId,SprintId,CompletedAt,StartedAt,1 FROM dbo.Task
    WHERE Status='DONE' AND CompletedAt IS NOT NULL;
END;
GO
IF OBJECT_ID('dbo.TaskCompletionHistoryMetadata','U') IS NULL
BEGIN
    CREATE TABLE dbo.TaskCompletionHistoryMetadata (
        Id INT NOT NULL PRIMARY KEY CHECK(Id=1),
        StartedAt DATETIME2(7) NOT NULL
    );
    INSERT dbo.TaskCompletionHistoryMetadata(Id,StartedAt) VALUES(1,SYSUTCDATETIME());
END;
GO
CREATE OR ALTER TRIGGER dbo.trg_Task_AfterInsertUpdate
ON dbo.Task AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.TaskId=i.TaskId
        WHERE ((d.TaskId IS NULL OR i.ReporterId<>d.ReporterId) AND NOT EXISTS
            (SELECT 1 FROM dbo.ProjectMember m WHERE m.ProjectId=i.ProjectId AND m.UserId=i.ReporterId))
        OR (i.AssigneeId IS NOT NULL AND (i.Status<>'DONE' OR d.TaskId IS NULL OR
            ISNULL(i.AssigneeId,0)<>ISNULL(d.AssigneeId,0)) AND NOT EXISTS
            (SELECT 1 FROM dbo.ProjectMember m WHERE m.ProjectId=i.ProjectId AND m.UserId=i.AssigneeId))
    ) THROW 50050,N'Người tạo/người thực hiện mới phải là thành viên dự án.',1;
    UPDATE t SET
        StartedAt=CASE WHEN i.Status<>'TODO' AND t.StartedAt IS NULL THEN SYSUTCDATETIME() ELSE t.StartedAt END,
        CompletedAt=CASE WHEN i.Status='DONE' THEN COALESCE(t.CompletedAt,SYSUTCDATETIME()) ELSE NULL END,
        ProgressPercent=CASE WHEN i.Status='DONE' THEN 100 WHEN d.Status='DONE' AND i.ProgressPercent=100 THEN 0 ELSE i.ProgressPercent END,
        UpdatedAt=SYSUTCDATETIME()
    FROM dbo.Task t JOIN inserted i ON i.TaskId=t.TaskId LEFT JOIN deleted d ON d.TaskId=i.TaskId
    WHERE d.TaskId IS NULL OR d.Status<>i.Status;

    INSERT dbo.TaskCompletionHistory(TaskId,ProjectId,SprintId,CompletedAt,StartedAt)
    SELECT t.TaskId,t.ProjectId,t.SprintId,t.CompletedAt,t.StartedAt
    FROM dbo.Task t JOIN inserted i ON i.TaskId=t.TaskId
    WHERE t.Status='DONE' AND t.CompletedAt IS NOT NULL AND NOT EXISTS
        (SELECT 1 FROM dbo.TaskCompletionHistory h WITH(UPDLOCK,HOLDLOCK) WHERE h.TaskId=t.TaskId);
END;
GO
CREATE OR ALTER VIEW dbo.vw_TaskFlow AS
SELECT t.TaskId,t.ProjectId,t.SprintId,t.Title,t.Status,t.AssigneeId,t.StartedAt,t.CompletedAt,
    CASE WHEN t.Status='DONE' AND t.StartedAt IS NOT NULL AND t.CompletedAt>=t.StartedAt AND t.CompletedAt<=SYSUTCDATETIME()
        THEN CAST(DATEDIFF_BIG(SECOND,t.StartedAt,t.CompletedAt)/86400.0 AS DECIMAL(10,2)) END AS CycleTimeDays,
    CASE WHEN t.Status<>'DONE' AND t.StartedAt IS NOT NULL AND t.StartedAt<=SYSUTCDATETIME()
        THEN CAST(DATEDIFF_BIG(SECOND,t.StartedAt,SYSUTCDATETIME())/86400.0 AS DECIMAL(10,2)) END AS WorkItemAgeDays
FROM dbo.Task t;
GO
CREATE OR ALTER VIEW dbo.vw_ProjectFlowSummary AS
SELECT p.ProjectId,p.ProjectName,
    (SELECT COUNT(*) FROM dbo.Task t WHERE t.ProjectId=p.ProjectId AND t.Status IN('IN_PROGRESS','IN_REVIEW')) AS WIP,
    (SELECT COUNT(*) FROM dbo.TaskCompletionHistory h WHERE h.ProjectId=p.ProjectId
        AND h.CompletedAt>=DATEADD(DAY,-7,SYSUTCDATETIME()) AND h.CompletedAt<=SYSUTCDATETIME()) AS Throughput7Days,
    (SELECT CAST(AVG(f.CycleTimeDays) AS DECIMAL(10,2)) FROM dbo.vw_TaskFlow f WHERE f.ProjectId=p.ProjectId) AS AvgCycleTimeDays,
    (SELECT CAST(AVG(f.WorkItemAgeDays) AS DECIMAL(10,2)) FROM dbo.vw_TaskFlow f WHERE f.ProjectId=p.ProjectId) AS AvgWorkItemAgeDays
FROM dbo.Project p;
GO
CREATE OR ALTER VIEW dbo.vw_SprintThroughput AS
SELECT s.ProjectId,s.SprintId,s.SprintName,s.Status AS SprintStatus,
    (SELECT COUNT(*) FROM dbo.Task t WHERE t.SprintId=s.SprintId) AS TotalTasks,
    (SELECT COUNT(*) FROM dbo.TaskCompletionHistory h WHERE h.SprintId=s.SprintId AND h.CompletedAt<=SYSUTCDATETIME()) AS DoneTasks,
    (SELECT CAST(AVG(f.CycleTimeDays) AS DECIMAL(10,2)) FROM dbo.vw_TaskFlow f WHERE f.SprintId=s.SprintId) AS AvgCycleTimeDays
FROM dbo.Sprint s;
GO
CREATE OR ALTER VIEW dbo.vw_OverdueTasks AS
SELECT t.TaskId,t.ProjectId,t.Title,t.Status,t.Priority,t.Deadline,t.AssigneeId,
    DATEDIFF(HOUR,t.Deadline,SYSUTCDATETIME()) AS OverdueHours
FROM dbo.Task t WHERE t.Deadline<SYSUTCDATETIME() AND t.Status<>'DONE';
GO
CREATE OR ALTER PROCEDURE dbo.sp_CaptureFlowMetric @SprintId BIGINT AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS(SELECT 1 FROM dbo.Sprint WHERE SprintId=@SprintId) THROW 50041,N'Sprint không tồn tại.',1;
    INSERT dbo.FlowMetric(SprintId,WIP,Throughput,CycleTime,AvgWorkItemAge,CapturedAt)
    SELECT @SprintId,
        (SELECT COUNT(*) FROM dbo.Task t WHERE t.SprintId=@SprintId AND t.Status IN('IN_PROGRESS','IN_REVIEW')),
        (SELECT COUNT(*) FROM dbo.TaskCompletionHistory h WHERE h.SprintId=@SprintId AND h.CompletedAt<=SYSUTCDATETIME()),
        (SELECT AVG(f.CycleTimeDays) FROM dbo.vw_TaskFlow f WHERE f.SprintId=@SprintId),
        (SELECT AVG(f.WorkItemAgeDays) FROM dbo.vw_TaskFlow f WHERE f.SprintId=@SprintId),SYSUTCDATETIME();
END;
GO
COMMIT TRANSACTION;
GO
