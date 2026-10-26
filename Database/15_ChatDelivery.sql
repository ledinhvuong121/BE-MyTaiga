USE ScrumbanDB;
GO
SET XACT_ABORT ON;
IF COL_LENGTH('dbo.ChatParticipant','LastReadMessageId') IS NULL
    ALTER TABLE dbo.ChatParticipant ADD LastReadMessageId BIGINT NOT NULL CONSTRAINT DF_CP_ReadMessage DEFAULT 0;
GO
IF COL_LENGTH('dbo.Message','PushedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Message ADD PushedAt DATETIME2(7) NULL;
    EXEC(N'UPDATE dbo.Message SET PushedAt=SYSUTCDATETIME()');
END;
GO

IF COL_LENGTH('dbo.ChatParticipant','IsActive') IS NULL
 ALTER TABLE dbo.ChatParticipant ADD IsActive BIT NOT NULL CONSTRAINT DF_CP_Active DEFAULT 1;
GO
