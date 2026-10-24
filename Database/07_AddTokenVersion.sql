USE ScrumbanDB;
GO

IF COL_LENGTH('dbo.User', 'TokenVersion') IS NULL
BEGIN
    ALTER TABLE dbo.[User]
    ADD TokenVersion INT NOT NULL
        CONSTRAINT DF_User_TokenVersion DEFAULT (0)
        WITH VALUES;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.User')
      AND name = 'CK_User_TokenVersion'
)
BEGIN
    ALTER TABLE dbo.[User]
    ADD CONSTRAINT CK_User_TokenVersion
        CHECK (TokenVersion >= 0);
END;
GO

SELECT Email, TokenVersion
FROM dbo.[User];