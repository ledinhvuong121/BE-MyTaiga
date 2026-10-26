# Bug — UC24–UC25

Chạy `Database/13_BugUtcWorkflow.sql` sau 12 để ResolvedAt/UpdatedAt mới dùng UTC.
Không tự chuyển timestamp cũ. Bearer JWT và membership đúng dự án bắt buộc.
Theo BUG_HANDLE, Member và Manager đều CRUD/phân công/đổi ưu tiên/trạng thái; Admin không bypass.
Bug có vòng đời độc lập với Sprint: có thể báo cáo trên Task của Sprint CLOSED nếu project còn ACTIVE.
Project CLOSED chỉ đọc, DELETED không đọc. ReporterId lấy từ JWT; create luôn OPEN.

| Method | URL | Body |
|---|---|---|
| POST | /api/v1/bugs | taskId,title,description,environment,severity,priority |
| POST | /api/v1/tasks/{id}/bugs | cùng body, không cần taskId |
| GET | /api/v1/projects/{id}/bugs?status=&severity=&priority=&assigneeId=&taskId=&keyword=&page=1&pageSize=20 | Không |
| GET | /api/v1/bugs/{id} | Bug, Task, metadata chứng cứ và lịch sử |
| PUT | /api/v1/bugs/{id} | title,description,environment,severity,priority |
| DELETE | /api/v1/bugs/{id} | Không, giữ Task/comments/files |
| PATCH | /api/v1/bugs/{id}/assignee | `{ "assigneeId":3 }`, null bỏ gán |
| PATCH | /api/v1/bugs/{id}/priority | `{ "priority":"HIGH" }` |
| PATCH | /api/v1/bugs/{id}/status | `{ "status":"IN_PROGRESS" }` |
| POST | /api/v1/bugs/{id}/close | Alias chuyển CLOSED theo cùng workflow |

Title bắt buộc<=255, description/bước tái hiện bắt buộc<=10000, environment tùy chọn<=500.
Severity TRIVIAL/MINOR/MAJOR/BLOCKER; priority LOW/MEDIUM/HIGH/CRITICAL (mặc định MEDIUM).
Assignee phải là thành viên hoạt động. Không đổi Task/Reporter qua PUT. Danh sách pageSize 1–100.

Workflow: OPEN -> IN_PROGRESS -> RESOLVED -> CLOSED; RESOLVED/CLOSED -> REOPENED -> IN_PROGRESS.
Trạng thái hợp lệ nhưng chuyển sai/repeat trả 409; enum sai 400. ResolvedAt sinh UTC khi RESOLVED,
giữ khi CLOSED, xóa khi REOPENED; lần resolve lại ghi thời điểm mới. Trước khi xử lý tiếp Bug
đang giao người đã rời/bị khóa, cần giao lại hoặc bỏ gán.

POST 201 với data Bug; mutation 200 (DELETE data=null). List data={items,total,page,pageSize}.
Bug gồm bugId,taskId,reporterId,assigneeId,title,description,environment,severity,priority,status,
createdAt,updatedAt,resolvedAt. GET detail data={bug,task,taskComments,taskAttachments,history}.
Comments/files là của Task theo schema, không giả là chứng cứ riêng Bug; tối đa 20 mục mỗi loại,
history tối đa 50 log mới nhất, mỗi nhóm có total. Không trả FilePath của server.
Upload/file/comment API sẽ hoàn thiện ở UC26–UC27; detail hiện đọc dữ liệu đã có.

Create thông báo Manager; assign thông báo người nhận khác actor; status thông báo reporter/assignee
còn membership hoạt động, loại trùng và chính actor. Ghi Notification và ActivityLog trong cùng transaction.
Không đẩy SignalR cho đến module thông báo. 401 token;403 quyền;404 scope/ID;409 workflow/assignee/project;
400 validation. Khóa project chống race với xóa Task/thành viên/project.

## Kiểm thử

Môi trường độc lập `docs/testing-projects.md`, script 13 đã chạy:

```powershell
python tests/bugs_integration.py
```

2026-10-26: 64 kiểm tra HTTP+SQL đạt. Gồm quyền, CRUD, enums/độ dài, filters/phân trang,
chống chéo dự án, workflow/UTC/ResolvedAt/reopen, thông báo đúng recipient, lịch sử reporter đã rời,
bảo toàn Task/chứng cứ, path không bị lộ, Sprint CLOSED/project CLOSED/DELETED và rollback.
