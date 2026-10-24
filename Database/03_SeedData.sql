	/* =====================================================================
   FILE 03: DỮ LIỆU NỀN BẮT BUỘC (Role, Permission, RolePermission, SystemConfig, tài khoản Admin)
   Chạy sau 02_Logic.sql.
   ===================================================================== */
USE ScrumbanDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Bốn vai trò
INSERT dbo.Role (RoleName, Scope, Description) VALUES
 ('ADMIN',   'SYSTEM',  N'Quản trị viên hệ thống (duy nhất 1 tài khoản)'),
 ('USER',    'SYSTEM',  N'Người dùng thông thường'),
 ('MANAGER', 'PROJECT', N'Quản lý dự án (kế thừa toàn bộ quyền Member)'),
 ('MEMBER',  'PROJECT', N'Thành viên dự án');
GO

-- 2. Danh mục quyền (theo Bảng 2.5 – phân quyền chức năng)
INSERT dbo.Permission (PermissionCode, ModuleName, Description) VALUES
 -- Cấp hệ thống
 ('ACCOUNT_PROFILE',     N'Quản lý tài khoản',      N'Đăng nhập, đăng xuất, khôi phục mật khẩu, hồ sơ cá nhân'),
 ('SYS_USER_MANAGE',     N'Quản trị hệ thống',      N'Tạo, sửa, khóa/mở khóa người dùng'),
 ('SYS_ROLE_MANAGE',     N'Quản trị hệ thống',      N'Quản lý vai trò hệ thống'),
 ('SYS_CONFIG_MANAGE',   N'Quản trị hệ thống',      N'Quản lý cấu hình hệ thống'),
 ('PROJECT_CREATE',      N'Dự án và thành viên',    N'Tạo dự án'),
 ('PROJECT_VIEW',        N'Dự án và thành viên',    N'Xem danh sách dự án đã tạo hoặc tham gia'),
 -- Quyền Member
 ('TASK_CRUD',           N'Quản lý Task',           N'Tạo, cập nhật, xóa, xem chi tiết Task'),
 ('BOARD_USE',           N'Scrumban Board',         N'Xem, kéo thả, lọc, tìm kiếm trên Board'),
 ('BUG_HANDLE',          N'Quản lý Issue/Bug',      N'Ghi nhận và xử lý Bug'),
 ('COLLAB_USE',          N'Cộng tác',               N'Bình luận, tệp đính kèm, thông báo'),
 ('CHAT_USE',            N'Nhắn tin',               N'Chat cá nhân, nhắn tin nhóm, chia sẻ tệp, thông báo chat'),
 -- Quyền riêng của Manager
 ('PROJECT_UPDATE',      N'Dự án và thành viên',    N'Cập nhật thông tin dự án'),
 ('PROJECT_CLOSE_DELETE',N'Dự án và thành viên',    N'Đóng, xóa dự án'),
 ('MEMBER_MANAGE',       N'Dự án và thành viên',    N'Thêm, loại bỏ thành viên'),
 ('MEMBER_PROMOTE',      N'Dự án và thành viên',    N'Nâng Member lên Manager'),
 ('DEPARTMENT_MANAGE',   N'Dự án và thành viên',    N'Quản lý phòng ban và vị trí chuyên môn'),
 ('BACKLOG_MANAGE',      N'Product Backlog',        N'Quản lý Product Backlog'),
 ('EPIC_MANAGE',         N'Epic',                   N'Quản lý Epic'),
 ('SPRINT_MANAGE',       N'Sprint',                 N'Lập kế hoạch và theo dõi Sprint'),
 ('TASK_ASSIGN',         N'Quản lý Task',           N'Giao Task, tạo Subtask, đặt Deadline và Priority, cảnh báo quá hạn'),
 ('WIP_MANAGE',          N'WIP Limit',              N'Thiết lập WIP Limit'),
 ('FLOW_METRIC_VIEW',    N'Flow Metrics',           N'Xem WIP, Throughput, Cycle Time, Work Item Age'),
 ('CHAT_ROOM_MANAGE',    N'Nhắn tin',               N'Tạo phòng chat dự án, quản lý thành viên phòng chat');
GO

-- 3. Gán quyền cho vai trò
-- ADMIN
INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM dbo.Role r JOIN dbo.Permission p
  ON p.PermissionCode IN ('ACCOUNT_PROFILE','SYS_USER_MANAGE','SYS_ROLE_MANAGE','SYS_CONFIG_MANAGE')
WHERE r.RoleName = 'ADMIN';

-- USER
INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM dbo.Role r JOIN dbo.Permission p
  ON p.PermissionCode IN ('ACCOUNT_PROFILE','PROJECT_CREATE','PROJECT_VIEW')
WHERE r.RoleName = 'USER';

-- MEMBER
INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM dbo.Role r JOIN dbo.Permission p
  ON p.PermissionCode IN ('PROJECT_VIEW','TASK_CRUD','BOARD_USE','BUG_HANDLE','COLLAB_USE','CHAT_USE')
WHERE r.RoleName = 'MEMBER';

-- MANAGER = toàn bộ quyền MEMBER + quyền quản lý dự án
INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT m.RoleId, rp.PermissionId
FROM dbo.Role m
JOIN dbo.Role mem ON mem.RoleName = 'MEMBER'
JOIN dbo.RolePermission rp ON rp.RoleId = mem.RoleId
WHERE m.RoleName = 'MANAGER';

INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId FROM dbo.Role r JOIN dbo.Permission p
  ON p.PermissionCode IN ('PROJECT_UPDATE','PROJECT_CLOSE_DELETE','MEMBER_MANAGE','MEMBER_PROMOTE',
                          'DEPARTMENT_MANAGE','BACKLOG_MANAGE','EPIC_MANAGE','SPRINT_MANAGE',
                          'TASK_ASSIGN','WIP_MANAGE','FLOW_METRIC_VIEW','CHAT_ROOM_MANAGE')
WHERE r.RoleName = 'MANAGER';
GO

-- 4. Cấu hình hệ thống
INSERT dbo.SystemConfig (ConfigKey, ConfigValue, Description) VALUES
 ('MAX_FAILED_LOGIN',            N'5',                          N'Số lần đăng nhập sai liên tiếp tối đa trước khi khóa tài khoản'),
 ('MIN_PASSWORD_LENGTH',         N'8',                          N'Độ dài mật khẩu tối thiểu'),
 ('RESET_TOKEN_EXPIRE_MINUTES',  N'30',                         N'Thời hạn mã đặt lại mật khẩu (phút)'),
 ('JWT_EXPIRE_MINUTES',          N'1440',                         N'Thời hạn Access Token (phút)'),
 ('MAX_FILE_SIZE_MB',            N'25',                         N'Dung lượng tối đa mỗi tệp đính kèm (MB)'),
 ('ALLOWED_FILE_EXTENSIONS',     N'.pdf,.docx,.xlsx,.pptx,.png,.jpg,.jpeg,.zip,.txt', N'Định dạng tệp cho phép tải lên'),
 ('MAX_COMMENT_LENGTH',          N'5000',                       N'Độ dài tối đa bình luận / tin nhắn (ký tự)'),
 ('DEADLINE_WARNING_HOURS',      N'24',                         N'Cảnh báo Task sắp đến hạn trước số giờ này'),
 ('DEFAULT_SPRINT_WEEKS',        N'2',                          N'Độ dài Sprint mặc định (tuần)');
GO

-- 5. Tài khoản Admin duy nhất.
--    PasswordHash dưới đây CHỈ LÀ CHỖ GIỮ CHỖ: hãy sinh hash Bcrypt thật bằng code backend rồi UPDATE lại.
INSERT dbo.[User] (FullName, Email, PasswordHash)
VALUES (N'Quản trị hệ thống', 'admin@scrumban.local', '$2a$11$REPLACE_THIS_WITH_A_REAL_BCRYPT_HASH_GENERATED_BY_BACKEND.');

INSERT dbo.UserRole (UserId, RoleId)
SELECT u.UserId, r.RoleId
FROM dbo.[User] u CROSS JOIN dbo.Role r
WHERE u.Email = 'admin@scrumban.local' AND r.RoleName = 'ADMIN';
GO

-- Kiểm tra nhanh
SELECT RoleName, Scope FROM dbo.Role;
SELECT RoleName, COUNT(*) AS SoQuyen FROM dbo.vw_RolePermission GROUP BY RoleName;
GO
