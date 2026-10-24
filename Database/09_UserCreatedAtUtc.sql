USE ScrumbanDB;
GO

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @DefaultName SYSNAME;
    DECLARE @Sql NVARCHAR(MAX);

    SELECT @DefaultName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.User')
      AND c.name = 'CreatedAt';

    IF @DefaultName IS NOT NULL
    BEGIN
        SET @Sql =
            N'ALTER TABLE dbo.[User] DROP CONSTRAINT '
            + QUOTENAME(@DefaultName) + N';';

        EXEC sys.sp_executesql @Sql;
    END;

    ALTER TABLE dbo.[User]
    ADD CONSTRAINT DF_User_CreatedAt
        DEFAULT (SYSUTCDATETIME()) FOR CreatedAt;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

SELECT dc.name, dc.definition
FROM sys.default_constraints dc
JOIN sys.columns c
    ON c.object_id = dc.parent_object_id
    AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.User')
  AND c.name = 'CreatedAt';