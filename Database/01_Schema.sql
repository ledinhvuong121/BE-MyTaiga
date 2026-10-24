/* =====================================================================
   ĐỒ ÁN CHUYÊN NGÀNH – NHÓM 8
   Hệ thống hỗ trợ quản lý và cộng tác trong dự án phần mềm theo mô hình Scrumban
   File 01: TẠO DATABASE + 28 BẢNG + RÀNG BUỘC + INDEX
   Nền tảng: SQL Server 2017 trở lên (dùng STRING_AGG ở file 02)
   Thứ tự chạy: 01_Schema -> 02_Logic -> 03_SeedData -> (04_SampleData tùy chọn)

   Quy ước:
   - Khóa chính: BIGINT IDENTITY(1,1) (theo từ điển dữ liệu mục 2.1.9)
   - Chuỗi tiếng Việt dùng NVARCHAR (VARCHAR không lưu được dấu); mã/enum/đường dẫn dùng VARCHAR
   - TEXT -> NVARCHAR(MAX), BOOLEAN -> BIT, DATETIME -> DATETIME2(0), CURRENT_TIMESTAMP -> GETDATE()
   - Cột/bảng "(bổ sung)" = đề xuất thêm so với ERD, đã đánh dấu * trong báo cáo
   ===================================================================== */

USE master;
GO
CREATE DATABASE ScrumbanDB COLLATE Vietnamese_CI_AS;
GO
USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;   -- bắt buộc cho filtered index
SET ANSI_NULLS ON;
GO

/* =====================================================================
   NHÓM 1 – QUẢN LÝ TÀI KHOẢN (User, Role, UserRole, PasswordResetToken)
   ===================================================================== */

-- 1. Role: ADMIN, USER (SYSTEM) và MANAGER, MEMBER (PROJECT)
CREATE TABLE dbo.Role (
    RoleId       BIGINT IDENTITY(1,1) NOT NULL,
    RoleName     VARCHAR(30)   NOT NULL,
    Scope        VARCHAR(10)   NOT NULL,               -- SYSTEM | PROJECT (bổ sung)
    Description  NVARCHAR(200) NULL,
    CONSTRAINT PK_Role            PRIMARY KEY (RoleId),
    CONSTRAINT UQ_Role_RoleName   UNIQUE (RoleName),
    CONSTRAINT CK_Role_Scope      CHECK (Scope IN ('SYSTEM','PROJECT')),
    CONSTRAINT UQ_Role_Id_Scope   UNIQUE (RoleId, Scope)   -- để bảng con ép Scope bằng FK kép
);
GO

-- 2. User
CREATE TABLE dbo.[User] (
    UserId            BIGINT IDENTITY(1,1) NOT NULL,
    FullName          NVARCHAR(100) NOT NULL,
    Email             VARCHAR(255)  NOT NULL,
    PasswordHash      VARCHAR(255)  NOT NULL,           -- Bcrypt
    Avatar            VARCHAR(500)  NULL,
    PhoneNumber       VARCHAR(20)   NULL,
    Status            TINYINT       NOT NULL CONSTRAINT DF_User_Status DEFAULT 1,   -- 1: hoạt động, 0: bị khóa
    FailedLoginCount  INT           NOT NULL CONSTRAINT DF_User_Failed DEFAULT 0,   -- (bổ sung)
    CreatedAt         DATETIME2(0)  NOT NULL CONSTRAINT DF_User_CreatedAt DEFAULT GETDATE(),
    UpdatedAt         DATETIME2(0)  NULL,
    CONSTRAINT PK_User          PRIMARY KEY (UserId),
    CONSTRAINT UQ_User_Email    UNIQUE (Email),
    CONSTRAINT CK_User_Status   CHECK (Status IN (0,1)),
    CONSTRAINT CK_User_Failed   CHECK (FailedLoginCount >= 0),
    CONSTRAINT CK_User_Email    CHECK (Email LIKE '%_@_%._%'),
    CONSTRAINT CK_User_FullName CHECK (LEN(LTRIM(RTRIM(FullName))) > 0)
);
GO

-- 3. UserRole: chỉ gán vai trò cấp HỆ THỐNG (Scope = SYSTEM), mỗi user đúng 1 vai trò
CREATE TABLE dbo.UserRole (
    UserId     BIGINT      NOT NULL,
    RoleId     BIGINT      NOT NULL,
    RoleScope  VARCHAR(10) NOT NULL CONSTRAINT DF_UserRole_Scope DEFAULT 'SYSTEM',
    CONSTRAINT PK_UserRole        PRIMARY KEY (UserId, RoleId),
    CONSTRAINT UQ_UserRole_User   UNIQUE (UserId),
    CONSTRAINT CK_UserRole_Scope  CHECK (RoleScope = 'SYSTEM'),
    CONSTRAINT FK_UserRole_User   FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId) ON DELETE CASCADE,
    CONSTRAINT FK_UserRole_Role   FOREIGN KEY (RoleId, RoleScope) REFERENCES dbo.Role(RoleId, Scope)
);
GO

-- 4. PasswordResetToken (bổ sung cho UC1.3 – khôi phục mật khẩu)
CREATE TABLE dbo.PasswordResetToken (
    TokenId     BIGINT IDENTITY(1,1) NOT NULL,
    UserId      BIGINT        NOT NULL,
    TokenHash   VARCHAR(255)  NOT NULL,                 -- lưu băm của mã, không lưu mã gốc
    ExpiresAt   DATETIME2(0)  NOT NULL,
    IsUsed      BIT           NOT NULL CONSTRAINT DF_PRT_IsUsed DEFAULT 0,
    CreatedAt   DATETIME2(0)  NOT NULL CONSTRAINT DF_PRT_CreatedAt DEFAULT GETDATE(),
    CONSTRAINT PK_PasswordResetToken PRIMARY KEY (TokenId),
    CONSTRAINT FK_PRT_User FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId) ON DELETE CASCADE,
    CONSTRAINT CK_PRT_Expire CHECK (ExpiresAt > CreatedAt)
);
CREATE INDEX IX_PRT_User ON dbo.PasswordResetToken(UserId, IsUsed, ExpiresAt);
GO

/* =====================================================================
   NHÓM 2 – DỰ ÁN VÀ THÀNH VIÊN
   (Project, Department, BoardColumn, Position, ProjectMember, ProjectMemberPosition)
   ===================================================================== */

-- 5. Project
CREATE TABLE dbo.Project (
    ProjectId    BIGINT IDENTITY(1,1) NOT NULL,
    ProjectName  NVARCHAR(200) NOT NULL,
    Description  NVARCHAR(MAX) NULL,
    StartDate    DATE          NULL,
    EndDate      DATE          NULL,
    Status       VARCHAR(20)   NOT NULL CONSTRAINT DF_Project_Status DEFAULT 'ACTIVE',  -- ACTIVE | CLOSED | DELETED (xóa mềm)
    CreatedBy    BIGINT        NOT NULL,
    CreatedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_Project_CreatedAt DEFAULT GETDATE(),
    UpdatedAt    DATETIME2(0)  NULL,
    CONSTRAINT PK_Project          PRIMARY KEY (ProjectId),
    CONSTRAINT UQ_Project_Name     UNIQUE (CreatedBy, ProjectName),          -- không trùng tên trong phạm vi người tạo
    CONSTRAINT FK_Project_Creator  FOREIGN KEY (CreatedBy) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Project_Status   CHECK (Status IN ('ACTIVE','CLOSED','DELETED')),
    CONSTRAINT CK_Project_Dates    CHECK (EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate),
    CONSTRAINT CK_Project_Name     CHECK (LEN(LTRIM(RTRIM(ProjectName))) > 0)
);
CREATE INDEX IX_Project_Status ON dbo.Project(Status);
GO

-- 6. Department (bổ sung, Epic 06)
CREATE TABLE dbo.Department (
    DepartmentId    BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId       BIGINT        NOT NULL,
    DepartmentName  NVARCHAR(100) NOT NULL,
    Description     NVARCHAR(500) NULL,
    CONSTRAINT PK_Department          PRIMARY KEY (DepartmentId),
    CONSTRAINT UQ_Department_Name     UNIQUE (ProjectId, DepartmentName),
    CONSTRAINT UQ_Department_Id_Proj  UNIQUE (DepartmentId, ProjectId),
    CONSTRAINT FK_Department_Project  FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId)
);
GO

-- 7. BoardColumn: cột của Scrumban Board. ColumnName chính là mã trạng thái của Task
CREATE TABLE dbo.BoardColumn (
    BoardColumnId  BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId      BIGINT       NOT NULL,
    ColumnName     VARCHAR(30)  NOT NULL,           -- TODO, IN_PROGRESS, IN_REVIEW, DONE (mặc định)
    Position       INT          NOT NULL,           -- thứ tự cột từ trái sang phải
    WipLimit       INT          NULL,               -- NULL = không giới hạn
    CONSTRAINT PK_BoardColumn         PRIMARY KEY (BoardColumnId),
    CONSTRAINT UQ_BoardColumn_Name    UNIQUE (ProjectId, ColumnName),
    CONSTRAINT FK_BoardColumn_Project FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    CONSTRAINT CK_BoardColumn_Wip     CHECK (WipLimit IS NULL OR WipLimit >= 1),
    CONSTRAINT CK_BoardColumn_Pos     CHECK (Position >= 1)
);
GO

-- 8. Position: vị trí chuyên môn (FE, BE, Tester, BA...) – khác với vai trò
CREATE TABLE dbo.Position (
    PositionId    BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId     BIGINT        NOT NULL,
    PositionName  NVARCHAR(100) NOT NULL,
    Description   NVARCHAR(500) NULL,
    CONSTRAINT PK_Position           PRIMARY KEY (PositionId),
    CONSTRAINT UQ_Position_Name      UNIQUE (ProjectId, PositionName),
    CONSTRAINT UQ_Position_Id_Proj   UNIQUE (PositionId, ProjectId),
    CONSTRAINT FK_Position_Project   FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId)
);
GO

-- 9. ProjectMember: N–N giữa User và Project, kèm vai trò trong dự án (MANAGER | MEMBER)
CREATE TABLE dbo.ProjectMember (
    ProjectId     BIGINT      NOT NULL,
    UserId        BIGINT      NOT NULL,
    RoleId        BIGINT      NOT NULL,
    RoleScope     VARCHAR(10) NOT NULL CONSTRAINT DF_PM_Scope DEFAULT 'PROJECT',
    DepartmentId  BIGINT      NULL,
    JoinedAt      DATETIME2(0) NOT NULL CONSTRAINT DF_PM_JoinedAt DEFAULT GETDATE(),   -- (bổ sung)
    CONSTRAINT PK_ProjectMember       PRIMARY KEY (ProjectId, UserId),                 -- 1 user chỉ 1 vai trò / dự án
    CONSTRAINT CK_PM_Scope            CHECK (RoleScope = 'PROJECT'),
    CONSTRAINT FK_PM_Project          FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    CONSTRAINT FK_PM_User             FOREIGN KEY (UserId)    REFERENCES dbo.[User](UserId),
    CONSTRAINT FK_PM_Role             FOREIGN KEY (RoleId, RoleScope) REFERENCES dbo.Role(RoleId, Scope),
    CONSTRAINT FK_PM_Department       FOREIGN KEY (DepartmentId, ProjectId) REFERENCES dbo.Department(DepartmentId, ProjectId)
);
CREATE INDEX IX_PM_User ON dbo.ProjectMember(UserId);          -- lọc "các dự án tôi tham gia"
GO

-- 10. ProjectMemberPosition: N–N giữa ProjectMember và Position (cùng dự án)
CREATE TABLE dbo.ProjectMemberPosition (
    ProjectId   BIGINT NOT NULL,
    UserId      BIGINT NOT NULL,
    PositionId  BIGINT NOT NULL,
    CONSTRAINT PK_PMP PRIMARY KEY (ProjectId, UserId, PositionId),
    CONSTRAINT FK_PMP_Member   FOREIGN KEY (ProjectId, UserId)     REFERENCES dbo.ProjectMember(ProjectId, UserId) ON DELETE CASCADE,
    CONSTRAINT FK_PMP_Position FOREIGN KEY (PositionId, ProjectId) REFERENCES dbo.Position(PositionId, ProjectId)
);
GO

/* =====================================================================
   NHÓM 3 – EPIC, PRODUCT BACKLOG, SPRINT, TASK, FLOW METRICS
   ===================================================================== */

-- 11. Epic
CREATE TABLE dbo.Epic (
    EpicId       BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId    BIGINT        NOT NULL,
    Title        NVARCHAR(255) NOT NULL,
    Description  NVARCHAR(MAX) NULL,
    Priority     INT           NOT NULL CONSTRAINT DF_Epic_Priority DEFAULT 3,   -- số nguyên, 1 là cao nhất
    Status       VARCHAR(20)   NOT NULL CONSTRAINT DF_Epic_Status DEFAULT 'OPEN',
    CreatedBy    BIGINT        NOT NULL,
    CreatedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_Epic_CreatedAt DEFAULT GETDATE(),
    UpdatedAt    DATETIME2(0)  NULL,
    CONSTRAINT PK_Epic            PRIMARY KEY (EpicId),
    CONSTRAINT UQ_Epic_Id_Proj    UNIQUE (EpicId, ProjectId),
    CONSTRAINT FK_Epic_Project    FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    CONSTRAINT FK_Epic_Creator    FOREIGN KEY (CreatedBy) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Epic_Priority   CHECK (Priority >= 1),
    CONSTRAINT CK_Epic_Status     CHECK (Status IN ('OPEN','IN_PROGRESS','DONE','CLOSED')),
    CONSTRAINT CK_Epic_Title      CHECK (LEN(LTRIM(RTRIM(Title))) > 0)
);
CREATE INDEX IX_Epic_Project ON dbo.Epic(ProjectId, Status, Priority);
GO

-- 12. ProductBacklogItem
CREATE TABLE dbo.ProductBacklogItem (
    PBIId        BIGINT IDENTITY(1,1) NOT NULL,
    EpicId       BIGINT        NOT NULL,
    Title        NVARCHAR(255) NOT NULL,
    Description  NVARCHAR(MAX) NULL,
    StoryPoint   INT           NOT NULL CONSTRAINT DF_PBI_SP DEFAULT 0,
    Priority     INT           NOT NULL CONSTRAINT DF_PBI_Priority DEFAULT 3,    -- 1 là cao nhất
    Status       VARCHAR(20)   NOT NULL CONSTRAINT DF_PBI_Status DEFAULT 'NEW',
    CreatedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_PBI_CreatedAt DEFAULT GETDATE(),   -- (bổ sung)
    UpdatedAt    DATETIME2(0)  NULL,
    CONSTRAINT PK_PBI          PRIMARY KEY (PBIId),
    CONSTRAINT FK_PBI_Epic     FOREIGN KEY (EpicId) REFERENCES dbo.Epic(EpicId),
    CONSTRAINT CK_PBI_SP       CHECK (StoryPoint >= 0),
    CONSTRAINT CK_PBI_Priority CHECK (Priority >= 1),
    CONSTRAINT CK_PBI_Status   CHECK (Status IN ('NEW','READY','IN_SPRINT','DONE')),
    CONSTRAINT CK_PBI_Title    CHECK (LEN(LTRIM(RTRIM(Title))) > 0)
);
CREATE INDEX IX_PBI_Epic ON dbo.ProductBacklogItem(EpicId, Priority);
GO

-- 13. Sprint
CREATE TABLE dbo.Sprint (
    SprintId    BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId   BIGINT        NOT NULL,
    SprintName  NVARCHAR(150) NOT NULL,
    SprintGoal  NVARCHAR(500) NULL,
    StartDate   DATE          NOT NULL,
    EndDate     DATE          NOT NULL,
    Status      VARCHAR(20)   NOT NULL CONSTRAINT DF_Sprint_Status DEFAULT 'PLANNED',
    CONSTRAINT PK_Sprint          PRIMARY KEY (SprintId),
    CONSTRAINT UQ_Sprint_Name     UNIQUE (ProjectId, SprintName),
    CONSTRAINT UQ_Sprint_Id_Proj  UNIQUE (SprintId, ProjectId),
    CONSTRAINT FK_Sprint_Project  FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    CONSTRAINT CK_Sprint_Status   CHECK (Status IN ('PLANNED','ACTIVE','CLOSED')),
    CONSTRAINT CK_Sprint_Dates    CHECK (EndDate >= StartDate)
);
-- Mỗi dự án tối đa 1 Sprint đang chạy (ACTIVE)
CREATE UNIQUE INDEX UX_Sprint_OneActive ON dbo.Sprint(ProjectId) WHERE Status = 'ACTIVE';
GO

-- 14. SprintBacklogItem: N–N giữa Sprint và PBI
CREATE TABLE dbo.SprintBacklogItem (
    SprintId  BIGINT NOT NULL,
    PBIId     BIGINT NOT NULL,
    AddedAt   DATETIME2(0) NOT NULL CONSTRAINT DF_SBI_AddedAt DEFAULT GETDATE(),
    CONSTRAINT PK_SBI PRIMARY KEY (SprintId, PBIId),
    CONSTRAINT FK_SBI_Sprint FOREIGN KEY (SprintId) REFERENCES dbo.Sprint(SprintId) ON DELETE CASCADE,
    CONSTRAINT FK_SBI_PBI    FOREIGN KEY (PBIId)    REFERENCES dbo.ProductBacklogItem(PBIId)
);
CREATE INDEX IX_SBI_PBI ON dbo.SprintBacklogItem(PBIId);
GO

-- 15. Task
CREATE TABLE dbo.Task (
    TaskId           BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId        BIGINT        NOT NULL,
    SprintId         BIGINT        NULL,
    EpicId           BIGINT        NULL,
    ParentTaskId     BIGINT        NULL,                  -- Subtask
    AssigneeId       BIGINT        NULL,
    ReporterId       BIGINT        NOT NULL,
    Title            NVARCHAR(255) NOT NULL,
    Description      NVARCHAR(MAX) NULL,
    Priority         VARCHAR(20)   NOT NULL CONSTRAINT DF_Task_Priority DEFAULT 'MEDIUM',
    Status           VARCHAR(30)   NOT NULL CONSTRAINT DF_Task_Status DEFAULT 'TODO',
    Deadline         DATETIME2(0)  NULL,
    ProgressPercent  INT           NOT NULL CONSTRAINT DF_Task_Progress DEFAULT 0,
    Position         INT           NOT NULL CONSTRAINT DF_Task_Position DEFAULT 0,      -- thứ tự thẻ trong cột
    StartedAt        DATETIME2(0)  NULL,
    CompletedAt      DATETIME2(0)  NULL,
    CreatedAt        DATETIME2(0)  NOT NULL CONSTRAINT DF_Task_CreatedAt DEFAULT GETDATE(),
    UpdatedAt        DATETIME2(0)  NULL,
    CONSTRAINT PK_Task            PRIMARY KEY (TaskId),
    CONSTRAINT UQ_Task_Id_Proj    UNIQUE (TaskId, ProjectId),
    CONSTRAINT FK_Task_Project    FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    -- Sprint / Epic / Task cha phải cùng dự án với Task (FK kép, bỏ qua khi cột con = NULL)
    CONSTRAINT FK_Task_Sprint     FOREIGN KEY (SprintId, ProjectId)     REFERENCES dbo.Sprint(SprintId, ProjectId),
    CONSTRAINT FK_Task_Epic       FOREIGN KEY (EpicId, ProjectId)       REFERENCES dbo.Epic(EpicId, ProjectId),
    CONSTRAINT FK_Task_Parent     FOREIGN KEY (ParentTaskId, ProjectId) REFERENCES dbo.Task(TaskId, ProjectId),
    -- Trạng thái phải là một cột có thật trên Board của dự án
    CONSTRAINT FK_Task_Column     FOREIGN KEY (ProjectId, Status) REFERENCES dbo.BoardColumn(ProjectId, ColumnName) ON UPDATE CASCADE,
    CONSTRAINT FK_Task_Assignee   FOREIGN KEY (AssigneeId) REFERENCES dbo.[User](UserId),
    CONSTRAINT FK_Task_Reporter   FOREIGN KEY (ReporterId) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Task_Priority   CHECK (Priority IN ('LOW','MEDIUM','HIGH','CRITICAL')),
    CONSTRAINT CK_Task_Progress   CHECK (ProgressPercent BETWEEN 0 AND 100),
    CONSTRAINT CK_Task_NotSelf    CHECK (ParentTaskId IS NULL OR ParentTaskId <> TaskId),
    CONSTRAINT CK_Task_Time       CHECK (CompletedAt IS NULL OR StartedAt IS NULL OR CompletedAt >= StartedAt),
    CONSTRAINT CK_Task_Title      CHECK (LEN(LTRIM(RTRIM(Title))) > 0)
);
CREATE INDEX IX_Task_Board    ON dbo.Task(ProjectId, Status, Position) INCLUDE (Title, AssigneeId, Priority);
CREATE INDEX IX_Task_Sprint   ON dbo.Task(SprintId)     WHERE SprintId IS NOT NULL;
CREATE INDEX IX_Task_Epic     ON dbo.Task(EpicId)       WHERE EpicId IS NOT NULL;
CREATE INDEX IX_Task_Parent   ON dbo.Task(ParentTaskId) WHERE ParentTaskId IS NOT NULL;
CREATE INDEX IX_Task_Assignee ON dbo.Task(AssigneeId, Status) WHERE AssigneeId IS NOT NULL;
CREATE INDEX IX_Task_Deadline ON dbo.Task(Deadline)     WHERE Deadline IS NOT NULL AND CompletedAt IS NULL;
GO

-- 16. FlowMetric: bản chụp chỉ số luồng công việc theo Sprint
CREATE TABLE dbo.FlowMetric (
    FlowMetricId    BIGINT IDENTITY(1,1) NOT NULL,
    SprintId        BIGINT        NOT NULL,
    WIP             INT           NOT NULL CONSTRAINT DF_FM_WIP DEFAULT 0,
    Throughput      INT           NOT NULL CONSTRAINT DF_FM_TP  DEFAULT 0,        -- số Task hoàn thành
    CycleTime       DECIMAL(10,2) NULL,                                           -- trung bình, đơn vị: ngày
    AvgWorkItemAge  DECIMAL(10,2) NULL,                                           -- (bổ sung) ngày
    CapturedAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_FM_CapturedAt DEFAULT GETDATE(),   -- (bổ sung)
    CONSTRAINT PK_FlowMetric   PRIMARY KEY (FlowMetricId),
    CONSTRAINT FK_FM_Sprint    FOREIGN KEY (SprintId) REFERENCES dbo.Sprint(SprintId) ON DELETE CASCADE,
    CONSTRAINT CK_FM_Values    CHECK (WIP >= 0 AND Throughput >= 0
                                      AND (CycleTime IS NULL OR CycleTime >= 0)
                                      AND (AvgWorkItemAge IS NULL OR AvgWorkItemAge >= 0))
);
CREATE INDEX IX_FM_Sprint ON dbo.FlowMetric(SprintId, CapturedAt DESC);
GO

/* =====================================================================
   NHÓM 4 – BUG, BÌNH LUẬN, TỆP ĐÍNH KÈM, THÔNG BÁO, NHẬT KÝ
   ===================================================================== */

-- 17. Bug (mỗi Bug gắn với một Task)
CREATE TABLE dbo.Bug (
    BugId        BIGINT IDENTITY(1,1) NOT NULL,
    TaskId       BIGINT        NOT NULL,
    ReporterId   BIGINT        NOT NULL,
    AssigneeId   BIGINT        NULL,
    Title        NVARCHAR(255) NOT NULL,
    Description  NVARCHAR(MAX) NOT NULL,                  -- bước tái hiện
    Environment  NVARCHAR(500) NULL,
    Severity     VARCHAR(10)   NOT NULL,
    Priority     VARCHAR(20)   NOT NULL CONSTRAINT DF_Bug_Priority DEFAULT 'MEDIUM',
    Status       VARCHAR(20)   NOT NULL CONSTRAINT DF_Bug_Status DEFAULT 'OPEN',
    CreatedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_Bug_CreatedAt DEFAULT GETDATE(),   -- (bổ sung)
    UpdatedAt    DATETIME2(0)  NULL,
    ResolvedAt   DATETIME2(0)  NULL,
    CONSTRAINT PK_Bug           PRIMARY KEY (BugId),
    CONSTRAINT FK_Bug_Task      FOREIGN KEY (TaskId)     REFERENCES dbo.Task(TaskId) ON DELETE CASCADE,
    CONSTRAINT FK_Bug_Reporter  FOREIGN KEY (ReporterId) REFERENCES dbo.[User](UserId),
    CONSTRAINT FK_Bug_Assignee  FOREIGN KEY (AssigneeId) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Bug_Severity  CHECK (Severity IN ('TRIVIAL','MINOR','MAJOR','BLOCKER')),
    CONSTRAINT CK_Bug_Priority  CHECK (Priority IN ('LOW','MEDIUM','HIGH','CRITICAL')),
    CONSTRAINT CK_Bug_Status    CHECK (Status IN ('OPEN','IN_PROGRESS','RESOLVED','CLOSED','REOPENED')),
    CONSTRAINT CK_Bug_Title     CHECK (LEN(LTRIM(RTRIM(Title))) > 0)
);
CREATE INDEX IX_Bug_Task     ON dbo.Bug(TaskId);
CREATE INDEX IX_Bug_Filter   ON dbo.Bug(Status, Severity) INCLUDE (TaskId, AssigneeId);
CREATE INDEX IX_Bug_Assignee ON dbo.Bug(AssigneeId) WHERE AssigneeId IS NOT NULL;
GO

-- 18. Comment (người dùng chỉ sửa/xóa bình luận của mình – kiểm tra ở tầng ứng dụng)
CREATE TABLE dbo.Comment (
    CommentId  BIGINT IDENTITY(1,1) NOT NULL,
    TaskId     BIGINT        NOT NULL,
    UserId     BIGINT        NOT NULL,
    Content    NVARCHAR(MAX) NOT NULL,
    CreatedAt  DATETIME2(0)  NOT NULL CONSTRAINT DF_Comment_CreatedAt DEFAULT GETDATE(),
    UpdatedAt  DATETIME2(0)  NULL,                          -- (bổ sung) khi sửa bình luận
    CONSTRAINT PK_Comment      PRIMARY KEY (CommentId),
    CONSTRAINT FK_Comment_Task FOREIGN KEY (TaskId) REFERENCES dbo.Task(TaskId) ON DELETE CASCADE,
    CONSTRAINT FK_Comment_User FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Comment_Len  CHECK (LEN(LTRIM(RTRIM(Content))) BETWEEN 1 AND 5000)
);
CREATE INDEX IX_Comment_Task ON dbo.Comment(TaskId, CreatedAt);
CREATE INDEX IX_Comment_User ON dbo.Comment(UserId);
GO

-- 19. Attachment
CREATE TABLE dbo.Attachment (
    AttachmentId  BIGINT IDENTITY(1,1) NOT NULL,
    TaskId        BIGINT        NOT NULL,
    FileName      NVARCHAR(255) NOT NULL,
    FilePath      VARCHAR(500)  NOT NULL,
    FileSize      BIGINT        NOT NULL,                  -- byte
    UploadedBy    BIGINT        NOT NULL,                  -- (bổ sung)
    UploadedAt    DATETIME2(0)  NOT NULL CONSTRAINT DF_Att_UploadedAt DEFAULT GETDATE(),   -- (bổ sung)
    CONSTRAINT PK_Attachment      PRIMARY KEY (AttachmentId),
    CONSTRAINT FK_Att_Task        FOREIGN KEY (TaskId)     REFERENCES dbo.Task(TaskId) ON DELETE CASCADE,
    CONSTRAINT FK_Att_Uploader    FOREIGN KEY (UploadedBy) REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Att_Size        CHECK (FileSize >= 0)
);
CREATE INDEX IX_Attachment_Task ON dbo.Attachment(TaskId);
GO

-- 20. Notification
--   Type gợi ý: TASK_ASSIGNED, TASK_UPDATED, COMMENT_ADDED, BUG_CREATED, BUG_ASSIGNED,
--               SPRINT_CREATED, DEADLINE_WARNING, MEMBER_ADDED, CHAT_MESSAGE, SYSTEM
CREATE TABLE dbo.Notification (
    NotificationId  BIGINT IDENTITY(1,1) NOT NULL,
    UserId          BIGINT        NOT NULL,
    Type            VARCHAR(30)   NOT NULL,
    Title           NVARCHAR(200) NULL,                    -- (bổ sung)
    Content         NVARCHAR(500) NULL,                    -- (bổ sung)
    RefType         VARCHAR(30)   NULL,                    -- (bổ sung) TASK | BUG | SPRINT | CHAT...
    RefId           BIGINT        NULL,                    -- (bổ sung) id đối tượng liên quan
    IsRead          BIT           NOT NULL CONSTRAINT DF_Noti_IsRead DEFAULT 0,
    CreatedAt       DATETIME2(0)  NOT NULL CONSTRAINT DF_Noti_CreatedAt DEFAULT GETDATE(),
    ReadAt          DATETIME2(0)  NULL,                    -- (bổ sung)
    CONSTRAINT PK_Notification PRIMARY KEY (NotificationId),
    CONSTRAINT FK_Noti_User    FOREIGN KEY (UserId) REFERENCES dbo.[User](UserId) ON DELETE CASCADE
);
CREATE INDEX IX_Noti_User ON dbo.Notification(UserId, IsRead, CreatedAt DESC);
GO

-- 21. ActivityLog (ProjectId = NULL cho thao tác cấp hệ thống như khóa tài khoản)
CREATE TABLE dbo.ActivityLog (
    ActivityLogId  BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId      BIGINT        NULL,
    UserId         BIGINT        NULL,                     -- NULL nếu hệ thống tự ghi
    Action         VARCHAR(50)   NOT NULL,                 -- (bổ sung) TASK_CREATED, TASK_MOVED...
    EntityType     VARCHAR(30)   NULL,                     -- (bổ sung)
    EntityId       BIGINT        NULL,                     -- (bổ sung)
    Description    NVARCHAR(500) NULL,                     -- (bổ sung)
    CreatedAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_Log_CreatedAt DEFAULT GETDATE(),
    CONSTRAINT PK_ActivityLog   PRIMARY KEY (ActivityLogId),
    CONSTRAINT FK_Log_Project   FOREIGN KEY (ProjectId) REFERENCES dbo.Project(ProjectId),
    CONSTRAINT FK_Log_User      FOREIGN KEY (UserId)    REFERENCES dbo.[User](UserId) ON DELETE SET NULL
);
CREATE INDEX IX_Log_Project ON dbo.ActivityLog(ProjectId, CreatedAt DESC);
CREATE INDEX IX_Log_User    ON dbo.ActivityLog(UserId, CreatedAt DESC);
CREATE INDEX IX_Log_Entity  ON dbo.ActivityLog(EntityType, EntityId);
GO

/* =====================================================================
   NHÓM 5 – NHẮN TIN VÀ TRAO ĐỔI TRỰC TIẾP
   ===================================================================== */

-- 22. ChatRoom: PRIVATE (1-1, không thuộc dự án) hoặc GROUP (nhóm theo dự án)
CREATE TABLE dbo.ChatRoom (
    ChatRoomId         BIGINT IDENTITY(1,1) NOT NULL,
    ProjectId          BIGINT       NULL,
    RoomType           VARCHAR(10)  NOT NULL,
    RoomName           NVARCHAR(100) NULL,                  -- (bổ sung) tên phòng nhóm
    PrivateUserLowId   BIGINT       NULL,                   -- (bổ sung) cặp user nhỏ/lớn để mỗi cặp chỉ có 1 phòng
    PrivateUserHighId  BIGINT       NULL,
    CreatedBy          BIGINT       NULL,                   -- (bổ sung)
    CreatedAt          DATETIME2(0) NOT NULL CONSTRAINT DF_Room_CreatedAt DEFAULT GETDATE(),
    CONSTRAINT PK_ChatRoom        PRIMARY KEY (ChatRoomId),
    CONSTRAINT FK_Room_Project    FOREIGN KEY (ProjectId)         REFERENCES dbo.Project(ProjectId),
    CONSTRAINT FK_Room_Low        FOREIGN KEY (PrivateUserLowId)  REFERENCES dbo.[User](UserId),
    CONSTRAINT FK_Room_High       FOREIGN KEY (PrivateUserHighId) REFERENCES dbo.[User](UserId),
    CONSTRAINT FK_Room_Creator    FOREIGN KEY (CreatedBy)         REFERENCES dbo.[User](UserId),
    CONSTRAINT CK_Room_Type       CHECK (RoomType IN ('PRIVATE','GROUP')),
    CONSTRAINT CK_Room_Shape      CHECK (
        (RoomType = 'PRIVATE' AND ProjectId IS NULL AND PrivateUserLowId IS NOT NULL
                              AND PrivateUserHighId IS NOT NULL AND PrivateUserLowId < PrivateUserHighId)
     OR (RoomType = 'GROUP'   AND ProjectId IS NOT NULL AND PrivateUserLowId IS NULL AND PrivateUserHighId IS NULL))
);
-- Mỗi cặp người dùng chỉ có tối đa một phòng chat riêng
CREATE UNIQUE INDEX UX_Room_PrivatePair ON dbo.ChatRoom(PrivateUserLowId, PrivateUserHighId) WHERE RoomType = 'PRIVATE';
CREATE INDEX IX_Room_Project ON dbo.ChatRoom(ProjectId) WHERE ProjectId IS NOT NULL;
GO

-- 23. ChatParticipant
CREATE TABLE dbo.ChatParticipant (
    ChatRoomId  BIGINT NOT NULL,
    UserId      BIGINT NOT NULL,
    JoinedAt    DATETIME2(0) NOT NULL CONSTRAINT DF_CP_JoinedAt DEFAULT GETDATE(),   -- (bổ sung)
    LastReadAt  DATETIME2(0) NULL,                                                   -- (bổ sung) đếm tin chưa đọc
    CONSTRAINT PK_ChatParticipant PRIMARY KEY (ChatRoomId, UserId),
    CONSTRAINT FK_CP_Room FOREIGN KEY (ChatRoomId) REFERENCES dbo.ChatRoom(ChatRoomId) ON DELETE CASCADE,
    CONSTRAINT FK_CP_User FOREIGN KEY (UserId)     REFERENCES dbo.[User](UserId)
);
CREATE INDEX IX_CP_User ON dbo.ChatParticipant(UserId);
GO

-- 24. Message (người gửi phải là thành viên phòng – FK kép tới ChatParticipant)
CREATE TABLE dbo.Message (
    MessageId   BIGINT IDENTITY(1,1) NOT NULL,
    ChatRoomId  BIGINT        NOT NULL,
    SenderId    BIGINT        NOT NULL,
    Content     NVARCHAR(MAX) NULL,                          -- NULL/rỗng khi chỉ gửi tệp
    SentAt      DATETIME2(0)  NOT NULL CONSTRAINT DF_Msg_SentAt DEFAULT GETDATE(),
    CONSTRAINT PK_Message PRIMARY KEY (MessageId),
    CONSTRAINT FK_Msg_Room   FOREIGN KEY (ChatRoomId) REFERENCES dbo.ChatRoom(ChatRoomId) ON DELETE CASCADE,
    CONSTRAINT FK_Msg_Sender FOREIGN KEY (ChatRoomId, SenderId) REFERENCES dbo.ChatParticipant(ChatRoomId, UserId),
    CONSTRAINT CK_Msg_Len    CHECK (Content IS NULL OR LEN(Content) <= 5000)
);
CREATE INDEX IX_Msg_Room ON dbo.Message(ChatRoomId, SentAt DESC);
GO

-- 25. MessageAttachment
CREATE TABLE dbo.MessageAttachment (
    MessageAttachmentId  BIGINT IDENTITY(1,1) NOT NULL,
    MessageId            BIGINT        NOT NULL,
    FileName             NVARCHAR(255) NOT NULL,
    FilePath             VARCHAR(500)  NOT NULL,
    FileSize             BIGINT        NOT NULL,
    CONSTRAINT PK_MessageAttachment PRIMARY KEY (MessageAttachmentId),
    CONSTRAINT FK_MA_Message FOREIGN KEY (MessageId) REFERENCES dbo.Message(MessageId) ON DELETE CASCADE,
    CONSTRAINT CK_MA_Size    CHECK (FileSize >= 0)
);
CREATE INDEX IX_MA_Message ON dbo.MessageAttachment(MessageId);
GO

/* =====================================================================
   NHÓM 6 – PHÂN QUYỀN VÀ CẤU HÌNH HỆ THỐNG
   ===================================================================== */

-- 26. Permission
CREATE TABLE dbo.Permission (
    PermissionId    BIGINT IDENTITY(1,1) NOT NULL,
    PermissionCode  VARCHAR(60)   NOT NULL,
    ModuleName      NVARCHAR(100) NULL,                    -- (bổ sung)
    Description     NVARCHAR(200) NULL,                    -- (bổ sung)
    CONSTRAINT PK_Permission      PRIMARY KEY (PermissionId),
    CONSTRAINT UQ_Permission_Code UNIQUE (PermissionCode)
);
GO

-- 27. RolePermission: quyền MANAGER bao gồm toàn bộ quyền MEMBER (nạp ở file 03)
CREATE TABLE dbo.RolePermission (
    RoleId        BIGINT NOT NULL,
    PermissionId  BIGINT NOT NULL,
    CONSTRAINT PK_RolePermission PRIMARY KEY (RoleId, PermissionId),
    CONSTRAINT FK_RP_Role       FOREIGN KEY (RoleId)       REFERENCES dbo.Role(RoleId)             ON DELETE CASCADE,
    CONSTRAINT FK_RP_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permission(PermissionId) ON DELETE CASCADE
);
CREATE INDEX IX_RP_Permission ON dbo.RolePermission(PermissionId);
GO

-- 28. SystemConfig
CREATE TABLE dbo.SystemConfig (
    ConfigKey    VARCHAR(100)  NOT NULL,
    ConfigValue  NVARCHAR(500) NOT NULL,
    Description  NVARCHAR(200) NULL,                       -- (bổ sung)
    UpdatedAt    DATETIME2(0)  NULL,                       -- (bổ sung)
    CONSTRAINT PK_SystemConfig PRIMARY KEY (ConfigKey)
);
GO

-- Kiểm tra: phải đủ 28 bảng
SELECT COUNT(*) AS SoBang FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';
GO
