USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
CREATE OR ALTER TRIGGER dbo.trg_Bug_AfterUpdate
ON dbo.Bug AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Status) RETURN;
    UPDATE b SET
        ResolvedAt=CASE WHEN i.Status IN('RESOLVED','CLOSED') THEN COALESCE(b.ResolvedAt,SYSUTCDATETIME()) ELSE NULL END,
        UpdatedAt=SYSUTCDATETIME()
    FROM dbo.Bug b JOIN inserted i ON i.BugId=b.BugId JOIN deleted d ON d.BugId=b.BugId
    WHERE i.Status<>d.Status;
END;
GO
