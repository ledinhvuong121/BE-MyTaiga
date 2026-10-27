USE ScrumbanDB;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Task','TagsJson') IS NULL ALTER TABLE dbo.Task ADD TagsJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Task','StoryPoints') IS NULL ALTER TABLE dbo.Task ADD StoryPoints INT NULL;
IF COL_LENGTH('dbo.Epic','Color') IS NULL ALTER TABLE dbo.Epic ADD Color VARCHAR(7) NULL;
IF COL_LENGTH('dbo.Notification','ActorId') IS NULL ALTER TABLE dbo.Notification ADD ActorId BIGINT NULL, ActorRole NVARCHAR(30) NULL, TargetTitle NVARCHAR(255) NULL;
IF COL_LENGTH('dbo.ActivityLog','ChangesJson') IS NULL ALTER TABLE dbo.ActivityLog ADD ChangesJson NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.Message','EditedAt') IS NULL ALTER TABLE dbo.Message ADD EditedAt DATETIME2(7) NULL, ReplyToMessageId BIGINT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Task_StoryPoints')
 ALTER TABLE dbo.Task ADD CONSTRAINT CK_Task_StoryPoints CHECK(StoryPoints IS NULL OR StoryPoints BETWEEN 0 AND 10000);
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.Task') AND name='TagsJson' AND max_length<>-1)
BEGIN
 IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Task_TagsJson') ALTER TABLE dbo.Task DROP CONSTRAINT CK_Task_TagsJson;
 ALTER TABLE dbo.Task ALTER COLUMN TagsJson NVARCHAR(MAX) NULL;
END;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='CK_Task_TagsJson')
 ALTER TABLE dbo.Task ADD CONSTRAINT CK_Task_TagsJson CHECK(TagsJson IS NULL OR ISJSON(TagsJson)=1);
COMMIT;
GO
