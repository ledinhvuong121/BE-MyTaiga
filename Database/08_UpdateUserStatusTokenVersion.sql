USE ScrumbanDB;
GO

CREATE OR ALTER PROCEDURE dbo.sp_SetUserStatus
    @AdminId BIGINT,
    @UserId BIGINT,
    @Status TINYINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Status IS NULL OR @Status NOT IN (0, 1)
        THROW 50082, N'Trạng thái không hợp lệ.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (
            SELECT 1
            FROM dbo.[User] u WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.UserRole ur ON ur.UserId = u.UserId
            JOIN dbo.Role r ON r.RoleId = ur.RoleId
            WHERE u.UserId = @AdminId
              AND u.Status = 1
              AND r.RoleName = 'ADMIN'
              AND r.Scope = 'SYSTEM'
        )
            THROW 50080, N'Chỉ Admin đang hoạt động được thực hiện.', 1;

        IF @AdminId = @UserId AND @Status = 0
            THROW 50081, N'Admin không thể khóa chính mình.', 1;

        UPDATE dbo.[User]
        SET Status = @Status,
            FailedLoginCount = 0,
            TokenVersion = CASE
                WHEN @Status = 0 THEN TokenVersion + 1
                ELSE TokenVersion
            END,
            UpdatedAt = SYSUTCDATETIME()
        WHERE UserId = @UserId;

        IF @@ROWCOUNT = 0
            THROW 50083, N'Tài khoản không tồn tại.', 1;

        INSERT dbo.ActivityLog (
            ProjectId, UserId, Action,
            EntityType, EntityId, Description, CreatedAt
        )
        VALUES (
            NULL,
            @AdminId,
            CASE WHEN @Status = 0
                THEN 'USER_LOCKED' ELSE 'USER_UNLOCKED' END,
            'USER',
            @UserId,
            CASE WHEN @Status = 0
                THEN N'Khóa tài khoản và thu hồi token.'
                ELSE N'Mở khóa tài khoản.' END,
            SYSUTCDATETIME()
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH;
END;
GO