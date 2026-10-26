USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
IF COL_LENGTH('dbo.SprintBacklogItem','StoryPointAtClose') IS NULL
    ALTER TABLE dbo.SprintBacklogItem ADD StoryPointAtClose INT NULL, StatusAtClose NVARCHAR(20) NULL;
GO
UPDATE b SET StoryPointAtClose=p.StoryPoint, StatusAtClose=p.Status
FROM dbo.SprintBacklogItem b JOIN dbo.Sprint s ON s.SprintId=b.SprintId AND s.Status='CLOSED'
JOIN dbo.ProductBacklogItem p ON p.PBIId=b.PBIId WHERE b.StatusAtClose IS NULL;
GO
IF OBJECT_ID('dbo.SprintBurndownEvent','U') IS NULL
BEGIN
    CREATE TABLE dbo.SprintBurndownEvent (
        EventId BIGINT IDENTITY PRIMARY KEY,
        SprintId BIGINT NOT NULL,
        PBIId BIGINT NOT NULL,
        DeltaPoints INT NOT NULL,
        DeltaRemaining INT NOT NULL,
        OccurredAt DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        IsBaseline BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_Burndown_Sprint FOREIGN KEY(SprintId) REFERENCES dbo.Sprint(SprintId) ON DELETE CASCADE
    );
    CREATE INDEX IX_Burndown_SprintTime ON dbo.SprintBurndownEvent(SprintId,OccurredAt,EventId);
    -- Existing memberships have no historical record: capture a baseline now, never invent older values.
    INSERT dbo.SprintBurndownEvent(SprintId,PBIId,DeltaPoints,DeltaRemaining,IsBaseline)
    SELECT b.SprintId,b.PBIId,p.StoryPoint,CASE WHEN p.Status='DONE' THEN 0 ELSE p.StoryPoint END,1
    FROM dbo.SprintBacklogItem b JOIN dbo.ProductBacklogItem p ON p.PBIId=b.PBIId;
END;
GO
CREATE OR ALTER TRIGGER dbo.trg_SprintBacklog_Burndown
ON dbo.SprintBacklogItem AFTER INSERT,DELETE AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.SprintBurndownEvent(SprintId,PBIId,DeltaPoints,DeltaRemaining)
    SELECT i.SprintId,i.PBIId,p.StoryPoint,CASE WHEN p.Status='DONE' THEN 0 ELSE p.StoryPoint END
    FROM inserted i JOIN dbo.ProductBacklogItem p ON p.PBIId=i.PBIId;
    INSERT dbo.SprintBurndownEvent(SprintId,PBIId,DeltaPoints,DeltaRemaining)
    SELECT d.SprintId,d.PBIId,-p.StoryPoint,CASE WHEN p.Status='DONE' THEN 0 ELSE -p.StoryPoint END
    FROM deleted d JOIN dbo.ProductBacklogItem p ON p.PBIId=d.PBIId
    JOIN dbo.Sprint s ON s.SprintId=d.SprintId WHERE s.Status<>'CLOSED';
END;
GO
CREATE OR ALTER TRIGGER dbo.trg_PBI_Burndown
ON dbo.ProductBacklogItem AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.SprintBurndownEvent(SprintId,PBIId,DeltaPoints,DeltaRemaining)
    SELECT b.SprintId,i.PBIId,i.StoryPoint-d.StoryPoint,
        (CASE WHEN i.Status='DONE' THEN 0 ELSE i.StoryPoint END)-
        (CASE WHEN d.Status='DONE' THEN 0 ELSE d.StoryPoint END)
    FROM inserted i JOIN deleted d ON d.PBIId=i.PBIId
    JOIN dbo.SprintBacklogItem b ON b.PBIId=i.PBIId
    JOIN dbo.Sprint s ON s.SprintId=b.SprintId AND s.Status<>'CLOSED'
    WHERE i.StoryPoint<>d.StoryPoint OR i.Status<>d.Status;
END;
GO
COMMIT TRANSACTION;
GO
