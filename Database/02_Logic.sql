/* =====================================================================
   FILE 02: TRIGGER + VIEW + STORED PROCEDURE
   Chạy sau 01_Schema.sql. Mã lỗi nghiệp vụ dùng THROW 50xxx.
   ===================================================================== */
USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   A. TRIGGER – các quy tắc nghiệp vụ mà CHECK/FK không biểu diễn được
   ===================================================================== */

-- A1. Tạo dự án: (1) Admin không được tạo dự án, (2) sinh 4 cột Board mặc định,
--     (3) gán người tạo làm MANAGER, (4) ghi ActivityLog.
CREATE TRIGGER dbo.trg_Project_AfterInsert
ON dbo.Project
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.UserRole ur ON ur.UserId = i.CreatedBy
               JOIN dbo.Role r      ON r.RoleId = ur.RoleId
               WHERE r.RoleName = 'ADMIN')
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50010, N'Admin chỉ quản trị hệ thống, không được tạo dự án.', 1;
    END

    INSERT dbo.BoardColumn (ProjectId, ColumnName, Position, WipLimit)
    SELECT i.ProjectId, c.ColumnName, c.Position, NULL
    FROM inserted i
    CROSS JOIN (VALUES ('TODO',1), ('IN_PROGRESS',2), ('IN_REVIEW',3), ('DONE',4)) AS c(ColumnName, Position);

    INSERT dbo.ProjectMember (ProjectId, UserId, RoleId)
    SELECT i.ProjectId, i.CreatedBy, r.RoleId
    FROM inserted i
    JOIN dbo.Role r ON r.RoleName = 'MANAGER' AND r.Scope = 'PROJECT';

    INSERT dbo.ActivityLog (ProjectId, UserId, Action, EntityType, EntityId, Description)
    SELECT i.ProjectId, i.CreatedBy, 'PROJECT_CREATED', 'PROJECT', i.ProjectId,
           N'Tạo dự án ' + i.ProjectName
    FROM inserted i;
END
GO

-- A2. Hệ thống chỉ có đúng 1 tài khoản ADMIN
CREATE TRIGGER dbo.trg_UserRole_SingleAdmin
ON dbo.UserRole
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF (SELECT COUNT(*)
        FROM dbo.UserRole ur
        JOIN dbo.Role r ON r.RoleId = ur.RoleId
        WHERE r.RoleName = 'ADMIN') > 1
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50011, N'Hệ thống chỉ cho phép một tài khoản Admin.', 1;
    END
END
GO

-- A3. Mỗi dự án luôn có ít nhất một MANAGER (chặn xóa/hạ quyền Manager cuối cùng)
CREATE TRIGGER dbo.trg_ProjectMember_KeepManager
ON dbo.ProjectMember
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM (SELECT DISTINCT ProjectId FROM deleted) d
        JOIN dbo.Project p ON p.ProjectId = d.ProjectId AND p.Status <> 'DELETED'
        WHERE NOT EXISTS (SELECT 1
                          FROM dbo.ProjectMember pm
                          JOIN dbo.Role r ON r.RoleId = pm.RoleId
                          WHERE pm.ProjectId = d.ProjectId AND r.RoleName = 'MANAGER'))
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50020, N'Dự án phải luôn có ít nhất một Manager.', 1;
    END
END
GO

-- A4. Sprint chưa CLOSED không được chồng lấn thời gian với Sprint chưa CLOSED khác trong cùng dự án
CREATE TRIGGER dbo.trg_Sprint_NoOverlap
ON dbo.Sprint
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN dbo.Sprint s ON s.ProjectId = i.ProjectId
                         AND s.SprintId <> i.SprintId
                         AND s.Status <> 'CLOSED'
                         AND s.StartDate <= i.EndDate
                         AND i.StartDate <= s.EndDate
        WHERE i.Status <> 'CLOSED')
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50040, N'Thời gian Sprint bị chồng lấn với một Sprint chưa kết thúc.', 1;
    END
END
GO

-- A5. Task: (1) người thực hiện và người tạo phải là thành viên dự án,
--     (2) tự động ghi StartedAt / CompletedAt khi đổi trạng thái (nguồn tính Cycle Time, Work Item Age).
CREATE TRIGGER dbo.trg_Task_AfterInsertUpdate
ON dbo.Task
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        WHERE (i.AssigneeId IS NOT NULL AND NOT EXISTS
                   (SELECT 1 FROM dbo.ProjectMember pm WHERE pm.ProjectId = i.ProjectId AND pm.UserId = i.AssigneeId))
           OR NOT EXISTS
                   (SELECT 1 FROM dbo.ProjectMember pm WHERE pm.ProjectId = i.ProjectId AND pm.UserId = i.ReporterId))
    BEGIN
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW 50050, N'Người tạo / người thực hiện Task phải là thành viên của dự án.', 1;
    END

    -- Chỉ xử lý dòng mới hoặc dòng đổi Status
    UPDATE t
    SET StartedAt = CASE WHEN i.Status = 'TODO'   THEN NULL
                         WHEN t.StartedAt IS NULL THEN SYSDATETIME()
                         ELSE t.StartedAt END,
        CompletedAt = CASE WHEN i.Status = 'DONE' THEN COALESCE(t.CompletedAt, SYSDATETIME()) ELSE NULL END,
        ProgressPercent = CASE WHEN i.Status = 'DONE' THEN 100 ELSE t.ProgressPercent END,
        UpdatedAt = SYSDATETIME()
    FROM dbo.Task t
    JOIN inserted i      ON i.TaskId = t.TaskId
    LEFT JOIN deleted d  ON d.TaskId = t.TaskId
    WHERE d.TaskId IS NULL OR d.Status <> i.Status;
END
GO

-- A6. Bug: tự ghi ResolvedAt khi chuyển sang RESOLVED/CLOSED, xóa khi mở lại
CREATE TRIGGER dbo.trg_Bug_AfterUpdate
ON dbo.Bug
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Status) RETURN;

    UPDATE b
    SET ResolvedAt = CASE WHEN i.Status IN ('RESOLVED','CLOSED') THEN COALESCE(b.ResolvedAt, SYSDATETIME()) ELSE NULL END,
        UpdatedAt  = SYSDATETIME()
    FROM dbo.Bug b
    JOIN inserted i ON i.BugId = b.BugId
    JOIN deleted  d ON d.BugId = b.BugId
    WHERE d.Status <> i.Status;
END
GO

/* =====================================================================
   B. VIEW – phục vụ Board, WIP Limit, Flow Metrics, báo cáo
   ===================================================================== */

-- B1. Số Task hiện có / giới hạn WIP của từng cột Board
CREATE VIEW dbo.vw_BoardColumnWip
AS
SELECT bc.ProjectId, bc.BoardColumnId, bc.ColumnName, bc.Position, bc.WipLimit,
       COUNT(t.TaskId) AS CurrentCount,
       CAST(CASE WHEN bc.WipLimit IS NOT NULL AND COUNT(t.TaskId) > bc.WipLimit THEN 1 ELSE 0 END AS BIT) AS IsOverLimit
FROM dbo.BoardColumn bc
LEFT JOIN dbo.Task t ON t.ProjectId = bc.ProjectId AND t.Status = bc.ColumnName
GROUP BY bc.ProjectId, bc.BoardColumnId, bc.ColumnName, bc.Position, bc.WipLimit;
GO

-- B2. Chỉ số luồng của từng Task: Cycle Time (Task đã xong) và Work Item Age (Task đang làm), đơn vị ngày
CREATE VIEW dbo.vw_TaskFlow
AS
SELECT t.TaskId, t.ProjectId, t.SprintId, t.Title, t.Status, t.AssigneeId, t.StartedAt, t.CompletedAt,
       CASE WHEN t.StartedAt IS NOT NULL AND t.CompletedAt IS NOT NULL
            THEN CAST(DATEDIFF(MINUTE, t.StartedAt, t.CompletedAt) / 1440.0 AS DECIMAL(10,2)) END AS CycleTimeDays,
       CASE WHEN t.StartedAt IS NOT NULL AND t.CompletedAt IS NULL
            THEN CAST(DATEDIFF(MINUTE, t.StartedAt, SYSDATETIME()) / 1440.0 AS DECIMAL(10,2)) END AS WorkItemAgeDays
FROM dbo.Task t;
GO

-- B3. Tổng quan Flow Metrics theo dự án (WIP hiện tại, Throughput 7 ngày, Cycle Time TB, Work Item Age TB)
CREATE VIEW dbo.vw_ProjectFlowSummary
AS
SELECT p.ProjectId, p.ProjectName,
       SUM(CASE WHEN t.Status IN ('IN_PROGRESS','IN_REVIEW') THEN 1 ELSE 0 END) AS WIP,
       SUM(CASE WHEN t.CompletedAt >= DATEADD(DAY, -7, SYSDATETIME()) THEN 1 ELSE 0 END) AS Throughput7Days,
       CAST(AVG(f.CycleTimeDays)    AS DECIMAL(10,2)) AS AvgCycleTimeDays,
       CAST(AVG(f.WorkItemAgeDays)  AS DECIMAL(10,2)) AS AvgWorkItemAgeDays
FROM dbo.Project p
LEFT JOIN dbo.Task t         ON t.ProjectId = p.ProjectId
LEFT JOIN dbo.vw_TaskFlow f  ON f.TaskId = t.TaskId
GROUP BY p.ProjectId, p.ProjectName;
GO

-- B4. Throughput theo Sprint (số Task DONE)
CREATE VIEW dbo.vw_SprintThroughput
AS
SELECT s.ProjectId, s.SprintId, s.SprintName, s.Status AS SprintStatus,
       COUNT(t.TaskId) AS TotalTasks,
       SUM(CASE WHEN t.Status = 'DONE' THEN 1 ELSE 0 END) AS DoneTasks,
       CAST(AVG(CASE WHEN t.Status = 'DONE' THEN f.CycleTimeDays END) AS DECIMAL(10,2)) AS AvgCycleTimeDays
FROM dbo.Sprint s
LEFT JOIN dbo.Task t        ON t.SprintId = s.SprintId
LEFT JOIN dbo.vw_TaskFlow f ON f.TaskId = t.TaskId
GROUP BY s.ProjectId, s.SprintId, s.SprintName, s.Status;
GO

-- B5. Task quá hạn (cảnh báo Deadline)
CREATE VIEW dbo.vw_OverdueTasks
AS
SELECT t.TaskId, t.ProjectId, t.Title, t.Status, t.Priority, t.Deadline, t.AssigneeId,
       DATEDIFF(HOUR, t.Deadline, SYSDATETIME()) AS OverdueHours
FROM dbo.Task t
WHERE t.Deadline IS NOT NULL AND t.Deadline < SYSDATETIME() AND t.Status <> 'DONE';
GO

-- B6. Thành viên dự án kèm vai trò, phòng ban, các vị trí chuyên môn
CREATE VIEW dbo.vw_ProjectMemberDetail
AS
SELECT pm.ProjectId, pm.UserId, u.FullName, u.Email, r.RoleName AS ProjectRole,
       d.DepartmentName, pm.JoinedAt,
       (SELECT STRING_AGG(ps.PositionName, N', ')
        FROM dbo.ProjectMemberPosition mp
        JOIN dbo.Position ps ON ps.PositionId = mp.PositionId
        WHERE mp.ProjectId = pm.ProjectId AND mp.UserId = pm.UserId) AS Positions
FROM dbo.ProjectMember pm
JOIN dbo.[User] u ON u.UserId = pm.UserId
JOIN dbo.Role r   ON r.RoleId = pm.RoleId
LEFT JOIN dbo.Department d ON d.DepartmentId = pm.DepartmentId;
GO

-- B7. Quyền hiệu lực của từng vai trò (dùng để kiểm tra phân quyền)
CREATE VIEW dbo.vw_RolePermission
AS
SELECT r.RoleName, r.Scope, p.PermissionCode, p.ModuleName, p.Description
FROM dbo.RolePermission rp
JOIN dbo.Role r       ON r.RoleId = rp.RoleId
JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId;
GO

/* =====================================================================
   C. STORED PROCEDURE
   ===================================================================== */

-- C1. Kéo thả Task sang cột khác, kiểm soát WIP Limit.
--     Vượt giới hạn: trả lỗi 50031 (cảnh báo) trừ khi @ForceOverWip = 1.
CREATE PROCEDURE dbo.sp_MoveTask
    @TaskId        BIGINT,
    @ToStatus      VARCHAR(30),
    @NewPosition   INT = 0,
    @UserId        BIGINT,
    @ForceOverWip  BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ProjectId BIGINT, @FromStatus VARCHAR(30), @Limit INT, @Count INT, @Msg NVARCHAR(400);

    BEGIN TRANSACTION;

    SELECT @ProjectId = ProjectId, @FromStatus = Status
    FROM dbo.Task WITH (UPDLOCK, HOLDLOCK)
    WHERE TaskId = @TaskId;

    IF @ProjectId IS NULL
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50030, N'Task không tồn tại.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectMember WHERE ProjectId = @ProjectId AND UserId = @UserId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50032, N'Bạn không phải thành viên của dự án này.', 1;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.BoardColumn WHERE ProjectId = @ProjectId AND ColumnName = @ToStatus)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50033, N'Cột đích không tồn tại trên Board của dự án.', 1;
    END

    SELECT @Limit = WipLimit
    FROM dbo.BoardColumn WITH (UPDLOCK)
    WHERE ProjectId = @ProjectId AND ColumnName = @ToStatus;

    IF @FromStatus <> @ToStatus AND @Limit IS NOT NULL AND @ForceOverWip = 0
    BEGIN
        SELECT @Count = COUNT(*) FROM dbo.Task WHERE ProjectId = @ProjectId AND Status = @ToStatus;
        IF @Count >= @Limit
        BEGIN
            SET @Msg = N'Cột ' + @ToStatus + N' đã đạt giới hạn WIP (' + CAST(@Limit AS NVARCHAR(10)) + N'). Không thể kéo thêm Task.';
            ROLLBACK TRANSACTION;
            THROW 50031, @Msg, 1;
        END
    END

    -- Nhường chỗ cho thẻ mới trong cột đích
    UPDATE dbo.Task SET Position = Position + 1
    WHERE ProjectId = @ProjectId AND Status = @ToStatus AND Position >= @NewPosition AND TaskId <> @TaskId;

    UPDATE dbo.Task SET Status = @ToStatus, Position = @NewPosition WHERE TaskId = @TaskId;

    INSERT dbo.ActivityLog (ProjectId, UserId, Action, EntityType, EntityId, Description)
    VALUES (@ProjectId, @UserId, 'TASK_MOVED', 'TASK', @TaskId,
            N'Chuyển Task từ ' + @FromStatus + N' sang ' + @ToStatus);

    COMMIT TRANSACTION;
END
GO

-- C2. Chụp chỉ số Flow Metrics của một Sprint vào bảng FlowMetric
CREATE PROCEDURE dbo.sp_CaptureFlowMetric
    @SprintId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Sprint WHERE SprintId = @SprintId)
        THROW 50041, N'Sprint không tồn tại.', 1;

    INSERT dbo.FlowMetric (SprintId, WIP, Throughput, CycleTime, AvgWorkItemAge)
    SELECT @SprintId,
           ISNULL(SUM(CASE WHEN f.Status IN ('IN_PROGRESS','IN_REVIEW') THEN 1 ELSE 0 END), 0),
           ISNULL(SUM(CASE WHEN f.Status = 'DONE' THEN 1 ELSE 0 END), 0),
           CAST(AVG(f.CycleTimeDays)   AS DECIMAL(10,2)),
           CAST(AVG(f.WorkItemAgeDays) AS DECIMAL(10,2))
    FROM dbo.vw_TaskFlow f
    WHERE f.SprintId = @SprintId;
END
GO

-- C3. Lấy (hoặc tạo mới) phòng chat riêng giữa 2 người dùng; mỗi cặp chỉ có 1 phòng
CREATE PROCEDURE dbo.sp_GetOrCreatePrivateChat
    @UserA       BIGINT,
    @UserB       BIGINT,
    @ChatRoomId  BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserA = @UserB THROW 50060, N'Không thể tạo phòng chat với chính mình.', 1;
    IF (SELECT COUNT(*) FROM dbo.[User] WHERE UserId IN (@UserA, @UserB) AND Status = 1) < 2
        THROW 50061, N'Người dùng không tồn tại hoặc đã bị khóa.', 1;

    DECLARE @Low BIGINT = CASE WHEN @UserA < @UserB THEN @UserA ELSE @UserB END;
    DECLARE @High BIGINT = CASE WHEN @UserA < @UserB THEN @UserB ELSE @UserA END;

    SELECT @ChatRoomId = ChatRoomId
    FROM dbo.ChatRoom
    WHERE RoomType = 'PRIVATE' AND PrivateUserLowId = @Low AND PrivateUserHighId = @High;

    IF @ChatRoomId IS NULL
    BEGIN
        BEGIN TRANSACTION;
        INSERT dbo.ChatRoom (ProjectId, RoomType, PrivateUserLowId, PrivateUserHighId, CreatedBy)
        VALUES (NULL, 'PRIVATE', @Low, @High, @UserA);
        SET @ChatRoomId = SCOPE_IDENTITY();
        INSERT dbo.ChatParticipant (ChatRoomId, UserId) VALUES (@ChatRoomId, @Low), (@ChatRoomId, @High);
        COMMIT TRANSACTION;
    END
END
GO

-- C4. Nâng Member lên Manager trong một dự án (chỉ Manager của dự án đó được thực hiện)
CREATE PROCEDURE dbo.sp_PromoteMember
    @ProjectId  BIGINT,
    @UserId     BIGINT,
    @ByUserId   BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectMember pm JOIN dbo.Role r ON r.RoleId = pm.RoleId
                   WHERE pm.ProjectId = @ProjectId AND pm.UserId = @ByUserId AND r.RoleName = 'MANAGER')
        THROW 50070, N'Chỉ Manager của dự án mới được nâng vai trò thành viên.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.ProjectMember WHERE ProjectId = @ProjectId AND UserId = @UserId)
        THROW 50071, N'Người dùng không phải thành viên của dự án.', 1;

    UPDATE pm SET RoleId = r.RoleId
    FROM dbo.ProjectMember pm
    JOIN dbo.Role r ON r.RoleName = 'MANAGER' AND r.Scope = 'PROJECT'
    WHERE pm.ProjectId = @ProjectId AND pm.UserId = @UserId;

    INSERT dbo.ActivityLog (ProjectId, UserId, Action, EntityType, EntityId, Description)
    VALUES (@ProjectId, @ByUserId, 'MEMBER_PROMOTED', 'USER', @UserId, N'Nâng thành viên lên Manager');
END
GO

-- C5. Khóa / mở khóa tài khoản (Admin không được khóa chính mình)
CREATE PROCEDURE dbo.sp_SetUserStatus
    @AdminId  BIGINT,
    @UserId   BIGINT,
    @Status   TINYINT          -- 0: khóa, 1: mở khóa
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.UserRole ur JOIN dbo.Role r ON r.RoleId = ur.RoleId
                   WHERE ur.UserId = @AdminId AND r.RoleName = 'ADMIN')
        THROW 50080, N'Chỉ Admin mới được khóa/mở khóa tài khoản.', 1;
    IF @AdminId = @UserId AND @Status = 0
        THROW 50081, N'Admin không thể khóa chính tài khoản của mình.', 1;

    UPDATE dbo.[User]
    SET Status = @Status, FailedLoginCount = 0, UpdatedAt = SYSDATETIME()
    WHERE UserId = @UserId;

    INSERT dbo.ActivityLog (ProjectId, UserId, Action, EntityType, EntityId, Description)
    VALUES (NULL, @AdminId, CASE WHEN @Status = 0 THEN 'USER_LOCKED' ELSE 'USER_UNLOCKED' END,
            'USER', @UserId, CASE WHEN @Status = 0 THEN N'Khóa tài khoản' ELSE N'Mở khóa tài khoản' END);
END
GO
